using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Hakaru.Localization;

public sealed record LanguageOption(string Code, string NativeName, string Culture)
{
    public override string ToString() => NativeName;
}

/// <summary>
/// 実行中に UI 言語を切り替えます。英語を土台に読み込み、選択言語をその上に重ねるので、
/// 未翻訳のキーは英語にフォールバックします。XAML は <c>{DynamicResource ...}</c> を使います。
/// </summary>
public static class LocalizationManager
{
    public static readonly IReadOnlyList<LanguageOption> Languages = new List<LanguageOption>
    {
        new("en",      "English",    "en-US"),
        new("ja",      "日本語",      "ja-JP"),
        new("zh-Hans", "简体中文",    "zh-Hans"),
        new("ko",      "한국어",      "ko-KR"),
        new("de",      "Deutsch",    "de-DE"),
        new("es",      "Español",    "es-ES"),
        new("fr",      "Français",   "fr-FR"),
    };

    public static LanguageOption Current { get; private set; } = Languages[0];

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>言語が変わったときに発火します（コードが組み立てる文字列の再描画用）。</summary>
    public static event Action? LanguageChanged;

    private static ResourceDictionary? _baseDict;   // 英語（土台）
    private static ResourceDictionary? _overlayDict; // 選択言語

    /// <summary>起動時に呼び出します。英語を土台として常時読み込みます。</summary>
    public static void Initialize(string? preferredCode)
    {
        _baseDict = Load("en");
        Application.Current.Resources.MergedDictionaries.Add(_baseDict);

        string code = Resolve(preferredCode);
        Apply(code);
    }

    public static void SetLanguage(string code)
    {
        if (code == Current.Code) return;
        Apply(code);
    }

    private static void Apply(string code)
    {
        var opt = Languages.FirstOrDefault(l => l.Code == code) ?? Languages[0];

        if (_overlayDict is not null)
            Application.Current.Resources.MergedDictionaries.Remove(_overlayDict);

        if (opt.Code != "en")
        {
            _overlayDict = Load(opt.Code);
            Application.Current.Resources.MergedDictionaries.Add(_overlayDict);
        }
        else
        {
            _overlayDict = null;
        }

        Current = opt;
        Culture = CultureInfo.GetCultureInfo(opt.Culture);
        Thread.CurrentThread.CurrentCulture = Culture;
        Thread.CurrentThread.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;

        LanguageChanged?.Invoke();
    }

    private static ResourceDictionary Load(string code) => new()
    {
        Source = new Uri($"/Hakaru;component/Localization/Strings.{code}.xaml", UriKind.Relative),
    };

    /// <summary>キーから訳文を取得します。見つからなければキー名をそのまま返します。</summary>
    public static string Get(string key)
        => Application.Current.TryFindResource(key) as string ?? key;

    /// <summary>書式文字列を取得して <see cref="string.Format(IFormatProvider,string,object?[])"/> します。</summary>
    public static string Format(string key, params object?[] args)
        => string.Format(Culture, Get(key), args);

    private static string Resolve(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && Languages.Any(l => l.Code == preferred))
            return preferred!;

        // OS の UI 言語から推測
        string ui = CultureInfo.CurrentUICulture.Name;         // 例: "ja-JP"
        string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; // 例: "ja"

        if (ui.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        return Languages.Any(l => l.Code == two) ? two : "en";
    }
}
