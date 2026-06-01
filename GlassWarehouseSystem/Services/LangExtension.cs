using System.Windows.Markup;

namespace GlassWarehouseSystem.Services
{
    /// <summary>
    /// XAML 标记扩展，用于多语言绑定
    /// 用法：{local:Lang KeyName}
    /// </summary>
    public class LangExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LangExtension() { }

        public LangExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return LanguageService.Get(Key);
        }
    }
}
