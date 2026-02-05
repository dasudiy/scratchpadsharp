using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.ViewModels;

public class SignatureParameterViewModel : ReactiveObject
{
    private string name = string.Empty;
    private string type = string.Empty;
    private bool isHighlighted;

    public string Name
    {
        get => name;
        set => this.RaiseAndSetIfChanged(ref name, value);
    }

    public string Type
    {
        get => type;
        set => this.RaiseAndSetIfChanged(ref type, value);
    }

    public bool IsHighlighted
    {
        get => isHighlighted;
        set => this.RaiseAndSetIfChanged(ref isHighlighted, value);
    }
}

public class MethodSignatureViewModel : ReactiveObject
{
    private string name = string.Empty;
    private string returnType = string.Empty;
    private string documentation = string.Empty;
    private ObservableCollection<SignatureParameterViewModel> parameters = new();
    private int currentParameterIndex = -1;

    public string Name
    {
        get => name;
        set => this.RaiseAndSetIfChanged(ref name, value);
    }

    public string ReturnType
    {
        get => returnType;
        set => this.RaiseAndSetIfChanged(ref returnType, value);
    }

    public string Documentation
    {
        get => documentation;
        set => this.RaiseAndSetIfChanged(ref documentation, value);
    }

    public ObservableCollection<SignatureParameterViewModel> Parameters
    {
        get => parameters;
        set => this.RaiseAndSetIfChanged(ref parameters, value);
    }

    public int CurrentParameterIndex
    {
        get => currentParameterIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref currentParameterIndex, value);
            UpdateParameterHighlighting();
        }
    }

    private void UpdateParameterHighlighting()
    {
        for (int i = 0; i < Parameters.Count; i++)
        {
            Parameters[i].IsHighlighted = (i == currentParameterIndex);
        }
    }
}

public class SignatureHelpViewModel : ReactiveObject
{
    private ObservableCollection<MethodSignatureViewModel> signatures = new();
    private int currentSignatureIndex;
    private bool isVisible;
    private double popupX;
    private double popupY;

    public ObservableCollection<MethodSignatureViewModel> Signatures
    {
        get => signatures;
        set => this.RaiseAndSetIfChanged(ref signatures, value);
    }

    public int CurrentSignatureIndex
    {
        get => currentSignatureIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref currentSignatureIndex, value);
            this.RaisePropertyChanged(nameof(CurrentSignature));
            this.RaisePropertyChanged(nameof(SignatureCountText));
        }
    }

    public bool IsVisible
    {
        get => isVisible;
        set => this.RaiseAndSetIfChanged(ref isVisible, value);
    }

    public double PopupX
    {
        get => popupX;
        set => this.RaiseAndSetIfChanged(ref popupX, value);
    }

    public double PopupY
    {
        get => popupY;
        set => this.RaiseAndSetIfChanged(ref popupY, value);
    }

    public MethodSignatureViewModel? CurrentSignature =>
        currentSignatureIndex >= 0 && currentSignatureIndex < signatures.Count
            ? signatures[currentSignatureIndex]
            : null;

    public string SignatureCountText =>
        signatures.Count > 0
            ? $"{currentSignatureIndex + 1} of {signatures.Count}"
            : "";

    public void UpdateSignatures(List<MethodSignature> methodSignatures, int argumentIndex)
    {
        var viewModels = new ObservableCollection<MethodSignatureViewModel>();

        foreach (var sig in methodSignatures)
        {
            var vm = new MethodSignatureViewModel
            {
                Name = sig.Name,
                ReturnType = sig.ReturnType,
                Documentation = ExtractSummary(sig.Documentation),
                CurrentParameterIndex = argumentIndex
            };

            foreach (var param in sig.Parameters)
            {
                vm.Parameters.Add(new SignatureParameterViewModel
                {
                    Name = param.Name,
                    Type = param.Type
                });
            }

            viewModels.Add(vm);
        }

        Signatures = viewModels;
        CurrentSignatureIndex = 0;
        this.RaisePropertyChanged(nameof(CurrentSignature));
        this.RaisePropertyChanged(nameof(SignatureCountText));
    }

    private string ExtractSummary(string xmlDoc)
    {
        if (string.IsNullOrEmpty(xmlDoc))
            return string.Empty;

        try
        {
            var summaryStart = xmlDoc.IndexOf("<summary>");
            var summaryEnd = xmlDoc.IndexOf("</summary>");
            
            if (summaryStart >= 0 && summaryEnd > summaryStart)
            {
                var summary = xmlDoc.Substring(summaryStart + 9, summaryEnd - summaryStart - 9);
                return summary.Trim();
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return string.Empty;
    }

    public void UpdateArgumentIndex(int argumentIndex)
    {
        if (CurrentSignature != null)
        {
            CurrentSignature.CurrentParameterIndex = argumentIndex;
        }
    }

    public void SelectNextSignature()
    {
        if (CurrentSignatureIndex < Signatures.Count - 1)
        {
            CurrentSignatureIndex++;
        }
    }

    public void SelectPreviousSignature()
    {
        if (CurrentSignatureIndex > 0)
        {
            CurrentSignatureIndex--;
        }
    }

    public void SetPosition(double x, double y)
    {
        PopupX = x;
        PopupY = y;
    }

    public void Show()
    {
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }
}
