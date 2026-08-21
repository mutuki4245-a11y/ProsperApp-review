namespace ProsperApp.Options;

public class AppOptions
{
    public string[] EnabledFeatures { get; set; } = [];

    /// <summary>
    /// 画面最上部に出す環境バナーの文言。空なら何も表示しない。
    /// 本番と同じ見た目のレビュー環境をテスト用DBに繋いで公開するため、
    /// どちらを触っているかを画面から判別できるようにする。
    /// </summary>
    public string EnvironmentBanner { get; set; } = string.Empty;
}
