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
    private EnhancedMethodSignature? selectedSignature;
    private int currentParameterIndex;
    private ObservableCollection<EnhancedMethodSignature> signatures = new();

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

    public ObservableCollection<EnhancedMethodSignature> Signatures
    {
        get => signatures;
        set
        {
            signatures = value;
            OnPropertyChanged();
        }
    }

    public EnhancedMethodSignature? SelectedSignature
    {
        get => selectedSignature;
        set
        {
            if (selectedSignature != value)
            {
                selectedSignature = value;
                OnPropertyChanged();

                if (selectedSignature != null)
                {
                    foreach (var sig in Signatures)
                    {
                        sig.IsBestMatch = (sig == selectedSignature);
                    }

                    UpdateParameterHighlights();
                    UpdateCurrentParameterDoc(selectedSignature);
                }
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

                if (Signatures.Count > 0)
                {
                    SelectBestMatchingOverload(currentParameterIndex);
                }
            }
        }
    }

    public void UpdateSignatures(List<MethodSignature> newSignatures, int parameterIndex)
    {
        var enhancedSignatures = newSignatures.Select(s => CreateEnhancedSignature(s)).ToList();
        Signatures = new ObservableCollection<EnhancedMethodSignature>(enhancedSignatures);

        currentParameterIndex = parameterIndex;
        OnPropertyChanged(nameof(CurrentParameterIndex));

        SelectBestMatchingOverload(parameterIndex);
    }

    public void UpdateArgumentIndex(int newIndex)
    {
        CurrentParameterIndex = newIndex;
    }

    public void Show()
    {
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    private EnhancedMethodSignature CreateEnhancedSignature(MethodSignature original)
    {
        var enhanced = new EnhancedMethodSignature
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
        if (Signatures.Count == 0)
        {
            SelectedSignature = null;
            return;
        }

        EnhancedMethodSignature? bestMatch = null;
        int minDiff = int.MaxValue;

        foreach (var sig in Signatures)
        {
            int paramCount = sig.Parameters.Count;
            bool hasParams = sig.Parameters.Any(p => p.IsParams);

            int diff;
            if (hasParams)
            {
                int requiredParams = sig.Parameters.TakeWhile(p => !p.IsParams).Count();
                if (parameterIndex >= requiredParams)
                    diff = 0;
                else
                    diff = requiredParams - parameterIndex;
            }
            else
            {
                if (parameterIndex < paramCount)
                    diff = 0;
                else
                    diff = parameterIndex - paramCount + 1;
            }

            int score = diff * 1000;
            score += Math.Abs(paramCount - (parameterIndex + 1));

            if (score < minDiff)
            {
                minDiff = score;
                bestMatch = sig;
            }
        }

        if (bestMatch != null)
        {
            SelectedSignature = bestMatch;
        }
        else if (SelectedSignature == null)
        {
            SelectedSignature = Signatures.FirstOrDefault(); // Fallback
            if (SelectedSignature != null) SelectedSignature.IsBestMatch = true;
        }
    }

    private void UpdateParameterHighlights()
    {
        if (SelectedSignature == null) return;

        foreach (var sig in Signatures)
        {
            for (int i = 0; i < sig.Parameters.Count; i++)
            {
                if (sig.Parameters[i] is EnhancedParameterSignature enhancedParam)
                {
                    bool isCurrentParam = (i == CurrentParameterIndex);
                    if (enhancedParam.IsParams && CurrentParameterIndex >= i)
                        isCurrentParam = true;

                    enhancedParam.IsHighlighted = isCurrentParam;
                }
            }
        }
    }

    private void UpdateCurrentParameterDoc(EnhancedMethodSignature sig)
    {
        // Find active param
        int idx = CurrentParameterIndex;
        // Logic: if index is within bounds, use it.
        // If params, and index is >= params index, use params doc.

        string doc = string.Empty;

        if (sig.Parameters.Count > 0)
        {
            if (idx >= 0 && idx < sig.Parameters.Count)
            {
                doc = sig.Parameters[idx].Documentation;
            }
            else if (sig.Parameters.Last().IsParams && idx >= sig.Parameters.Count - 1)
            {
                doc = sig.Parameters.Last().Documentation;
            }
        }

        // Format: "ParamName: Doc"
        if (!string.IsNullOrEmpty(doc))
        {
            // Get param name too?
            string paramName = "";
            if (idx >= 0 && idx < sig.Parameters.Count) paramName = sig.Parameters[idx].Name;
            else if (sig.Parameters.Count > 0 && sig.Parameters.Last().IsParams) paramName = sig.Parameters.Last().Name;

            if (!string.IsNullOrEmpty(paramName))
            {
                // sig.CurrentParameterDocumentation = $"{paramName}: {doc}";
                // Actually user might just want the doc.
                // But usually "value: The value to write" is better.
                // Since I don't have easy access to Name here without recalc, I'll rely on what I have.
                // Ah, I have the param object.
                sig.CurrentParameterDocumentation = doc;
            }
            else
            {
                sig.CurrentParameterDocumentation = doc;
            }
        }
        else
        {
            sig.CurrentParameterDocumentation = string.Empty;
        }

        // Also if doc is empty, try to get from dictionary if not populated in params?
        // MethodSignature has ParameterDocs dict.
        // But CreateEnhancedSignature populates Parameters.Documentation from it.
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class EnhancedMethodSignature : MethodSignature, INotifyPropertyChanged
{
    private bool isBestMatch;
    private string currentParameterDocumentation = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBestMatch
    {
        get => isBestMatch;
        set
        {
            if (isBestMatch != value)
            {
                isBestMatch = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentParameterDocumentation
    {
        get => currentParameterDocumentation;
        set
        {
            if (currentParameterDocumentation != value)
            {
                currentParameterDocumentation = value;
                OnPropertyChanged();
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
