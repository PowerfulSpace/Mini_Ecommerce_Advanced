using System.Globalization;
using System.Runtime.CompilerServices;
using FluentValidation;

namespace PS.MiniEcommerce.UnitTests;

internal static class FluentValidationCultureSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.InvariantCulture;
    }
}
