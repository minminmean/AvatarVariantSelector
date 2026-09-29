using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// ビルドを続けられない設定を見つけたときに投げる例外。
    ///
    /// プラグインの入口で受け止めて <see cref="AvatarVariantBuildError"/> として報告する。
    /// それ以外の例外は想定外の不具合なので、受け止めずに NDMF の内部エラーとして扱わせる。
    /// </summary>
    internal class AvatarVariantBuildException : Exception
    {
        public AvatarVariantBuildException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// NDMF のエラーレポートに載せるビルドエラー。
    ///
    /// 例外のまま NDMF に渡すと、スタックトレース付きの「内部エラー」として表示され、
    /// 肝心の文言が埋もれてしまう。設定の問題として普通のエラー表示に載せるためにこれを使う。
    /// 深刻度が Error なので、報告するだけでアップロードは止まる。
    ///
    /// 文言は AVS の辞書で組み立て済みのものを受け取る。NDMF の辞書は引かないので、
    /// Format 系のメソッドを上書きして、受け取った文字列をそのまま返す。
    /// </summary>
    internal class AvatarVariantBuildError : SimpleError
    {
        // SimpleError が要求するので持つだけで、実際には何も引かない。
        private static Localizer _emptyLocalizer;

        private readonly string _title;
        private readonly string _details;

        public AvatarVariantBuildError(string message)
        {
            // 1 行目を見出し、残りを詳細として出す。
            int newline = message.IndexOf('\n');
            if (newline < 0)
            {
                _title = message;
                _details = null;
            }
            else
            {
                _title = message.Substring(0, newline);
                _details = message.Substring(newline + 1);
            }
        }

        public override Localizer Localizer => _emptyLocalizer ??= new Localizer("en-US",
            () => new List<(string, Func<string, string>)> { ("en-US", key => null) });

        public override ErrorSeverity Severity => ErrorSeverity.Error;
        public override string TitleKey => "minminmart.avatar-variant.build-error";

        public override string FormatTitle() => _title;
        public override string FormatDetails() => _details;
        public override string FormatHint() => null;
    }
}
