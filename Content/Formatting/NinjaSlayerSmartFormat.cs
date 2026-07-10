using System.Globalization;
using SmartFormat.Core.Extensions;
using STS2RitsuLib.Interop.AutoRegistration;

namespace NinjaSlayer.Content.Formatting;

[RegisterSmartFormatSource]
public sealed class NinjaSlayerSmartFormatSource : ISource
{
    public bool TryEvaluateSelector(ISelectorInfo selectorInfo)
    {
        if (selectorInfo.SelectorText != "NinjaSlayerVersion")
        {
            return false;
        }

        selectorInfo.Result = NinjaSlayerVersion.Current;
        return true;
    }
}

[RegisterSmartFormatter]
public sealed class SignedNumberFormatter : IFormatter
{
    public string Name
    {
        get => "signed";
        set => throw new NotSupportedException();
    }

    public bool CanAutoDetect { get; set; }

    public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
    {
        if (!TryConvertToDecimal(formattingInfo.CurrentValue, out decimal value))
        {
            return false;
        }

        string text = value > 0
            ? "+" + value.ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
        formattingInfo.Write(text);
        return true;
    }

    private static bool TryConvertToDecimal(object? value, out decimal number)
    {
        try
        {
            if (value is not IConvertible)
            {
                number = 0;
                return false;
            }

            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            number = 0;
            return false;
        }
        catch (InvalidCastException)
        {
            number = 0;
            return false;
        }
        catch (OverflowException)
        {
            number = 0;
            return false;
        }
    }
}
