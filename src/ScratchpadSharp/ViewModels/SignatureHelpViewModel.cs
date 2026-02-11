using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.ViewModels;

public class SignatureHelpViewModel : INotifyPropertyChanged
{
    private bool isVisible;
    private int currentSignatureIndex;
    private int currentParameterIndex;
    private ObservableCollection<MethodSignature> signatures = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (isVisible != value)
            {
                isVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<MethodSignature> Signatures
    {
        get => signatures;
        set
        {
            signatures = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SignatureCountText));
            OnPropertyChanged(nameof(HasMultipleSignatures));
        }
    }

    public int CurrentSignatureIndex
    {
        get => currentSignatureIndex;
        set
        {
            if (currentSignatureIndex != value && value >= 0 && value < Signatures.Count)
            {
                currentSignatureIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentSignature));
                OnPropertyChanged(nameof(SignatureCountText));
                UpdateParameterHighlights();
            }
        }
    }

    public int CurrentParameterIndex
    {
        get => currentParameterIndex;
        set
        {
            if (currentParameterIndex != value)
            {
                currentParameterIndex = value;
                OnPropertyChanged();
                UpdateParameterHighlights();
            }
        }
    }

    public MethodSignature? CurrentSignature =>
        Signatures.Count > 0 && CurrentSignatureIndex >= 0 && CurrentSignatureIndex < Signatures.Count
            ? Signatures[CurrentSignatureIndex]
            : null;

    public string SignatureCountText =>
        Signatures.Count > 1
            ? $"↑↓ {CurrentSignatureIndex + 1} of {Signatures.Count} overloads"
            : string.Empty;

    public bool HasMultipleSignatures => Signatures.Count > 1;

    public void UpdateSignatures(List<MethodSignature> newSignatures, int parameterIndex)
    {
        Signatures = new ObservableCollection<MethodSignature>(
            newSignatures.Select(s => CreateEnhancedSignature(s)));

        CurrentSignatureIndex = 0;
        CurrentParameterIndex = parameterIndex;

        // 尝试选择最匹配的重载
        SelectBestMatchingOverload(parameterIndex);
    }

    public void UpdateArgumentIndex(int newIndex)
    {
        CurrentParameterIndex = newIndex;
    }

    public void SelectNextSignature()
    {
        if (Signatures.Count > 1)
        {
            CurrentSignatureIndex = (CurrentSignatureIndex + 1) % Signatures.Count;
        }
    }

    public void SelectPreviousSignature()
    {
        if (Signatures.Count > 1)
        {
            CurrentSignatureIndex = CurrentSignatureIndex == 0
                ? Signatures.Count - 1
                : CurrentSignatureIndex - 1;
        }
    }

    public void Show()
    {
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    private MethodSignature CreateEnhancedSignature(MethodSignature original)
    {
        var enhanced = new MethodSignature
        {
            Name = original.Name,
            ReturnType = original.ReturnType,
            Documentation = original.Documentation,
            Summary = original.Summary,
            IsExtensionMethod = original.IsExtensionMethod,
            FullSignature = original.FullSignature,
            ParameterDocs = new Dictionary<string, string>(original.ParameterDocs),
            Parameters = new List<ParameterSignature>()
        };

        // 复制参数并添加UI属性
        for (int i = 0; i < original.Parameters.Count; i++)
        {
            var param = original.Parameters[i];
            var enhancedParam = new EnhancedParameterSignature
            {
                Name = param.Name,
                Type = param.Type,
                Documentation = param.Documentation,
                IsParams = param.IsParams,
                IsOptional = param.IsOptional,
                DefaultValue = param.DefaultValue,
                IsHighlighted = false,
                IsLast = i == original.Parameters.Count - 1
            };
            enhanced.Parameters.Add(enhancedParam);
        }

        return enhanced;
    }

    private void SelectBestMatchingOverload(int parameterIndex)
    {
        if (Signatures.Count <= 1)
            return;

        // 选择参数数量最接近的重载
        int bestIndex = 0;
        int minDiff = int.MaxValue;

        for (int i = 0; i < Signatures.Count; i++)
        {
            var sig = Signatures[i];
            int paramCount = sig.Parameters.Count;

            // 如果有params参数,视为可以接受任意数量
            bool hasParams = sig.Parameters.Any(p => p.IsParams);

            int diff;
            if (hasParams)
            {
                // 如果有params,只要参数数量>=必需参数就是完美匹配
                int requiredParams = sig.Parameters.TakeWhile(p => !p.IsParams).Count();
                diff = parameterIndex >= requiredParams ? 0 : requiredParams - parameterIndex;
            }
            else
            {
                diff = Math.Abs(paramCount - (parameterIndex + 1));
            }

            if (diff < minDiff)
            {
                minDiff = diff;
                bestIndex = i;
            }
        }

        CurrentSignatureIndex = bestIndex;
    }

    private void UpdateParameterHighlights()
    {
        var current = CurrentSignature;
        if (current == null)
            return;

        for (int i = 0; i < current.Parameters.Count; i++)
        {
            if (current.Parameters[i] is EnhancedParameterSignature enhancedParam)
            {
                enhancedParam.IsHighlighted = (i == CurrentParameterIndex);
            }
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class EnhancedParameterSignature : ParameterSignature, INotifyPropertyChanged
{
    private bool isHighlighted;
    private bool isLast;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsHighlighted
    {
        get => isHighlighted;
        set
        {
            if (isHighlighted != value)
            {
                isHighlighted = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLast
    {
        get => isLast;
        set
        {
            if (isLast != value)
            {
                isLast = value;
                OnPropertyChanged();
            }
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
