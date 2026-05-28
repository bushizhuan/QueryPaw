using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SqlAnalyzer.Core.Models;

public sealed class TableColumnDefinition : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _dataType = string.Empty;
    private int? _length;
    private int? _precision;
    private int? _scale;
    private bool _isPrimaryKey;
    private bool _isNullable = true;
    private string _comment = string.Empty;
    private string _keyMarker = string.Empty;
    private IReadOnlyList<TableColumnTypeOption> _availableDataTypeOptions = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string DataType
    {
        get => _dataType;
        set
        {
            if (SetProperty(ref _dataType, value))
            {
                OnPropertyChanged(nameof(SelectedDataTypeOption));
            }
        }
    }

    public int? Length
    {
        get => _length;
        set => SetProperty(ref _length, value);
    }

    public int? Precision
    {
        get => _precision;
        set => SetProperty(ref _precision, value);
    }

    public int? Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public bool IsPrimaryKey
    {
        get => _isPrimaryKey;
        set => SetProperty(ref _isPrimaryKey, value);
    }

    public bool IsNullable
    {
        get => _isNullable;
        set
        {
            if (SetProperty(ref _isNullable, value))
            {
                OnPropertyChanged(nameof(IsNotNull));
            }
        }
    }

    public string Comment
    {
        get => _comment;
        set => SetProperty(ref _comment, value);
    }

    public string KeyMarker
    {
        get => _keyMarker;
        set => SetProperty(ref _keyMarker, value);
    }

    [JsonIgnore]
    public IReadOnlyList<TableColumnTypeOption> AvailableDataTypeOptions
    {
        get => _availableDataTypeOptions;
        set
        {
            if (SetProperty(ref _availableDataTypeOptions, value))
            {
                OnPropertyChanged(nameof(SelectedDataTypeOption));
            }
        }
    }

    [JsonIgnore]
    public TableColumnTypeOption? SelectedDataTypeOption
    {
        get
        {
            foreach (TableColumnTypeOption option in AvailableDataTypeOptions)
            {
                if (string.Equals(option.Name, DataType, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return null;
        }
        set
        {
            if (value != null)
            {
                DataType = value.Name;
            }
        }
    }

    public bool IsNotNull
    {
        get => !IsNullable;
        set => IsNullable = !value;
    }
    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
