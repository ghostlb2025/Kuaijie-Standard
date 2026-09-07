namespace Miashot
{
    internal static class ProductInfo
    {
        internal const string DisplayName = "快截";
        internal const string TrayDescription = "快截标准版 - 快速截图与完整标注";
        internal const string MutexName = @"Local\Kuaijie.Standard.SingleInstance";
        internal const string StartupValueName = "快截标准版";
        internal const string SettingsFolderName = "Kuaijie-Standard";
        internal const string ScreenshotPrefix = "快截_";

        internal static readonly string[] LegacySettingsFolderNames =
        {
            "Kuaijie",
            "Miashot-ProOCR",
            "Miashot"
        };

        internal static readonly string[] LegacyStartupValueNames =
        {
            "Miashot ProOCR",
            "Miashot"
        };
    }
}
