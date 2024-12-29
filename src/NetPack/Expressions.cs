namespace NetPack;

using System.Text.RegularExpressions;

partial class Expressions
{
    // <link rel="stylesheet" href="style.scss">
    [GeneratedRegex(@"<link\s+rel=""stylesheet""\s+href=""([^'""]+)"">", RegexOptions.Compiled)]
    public static partial Regex HtmlLink();

    // <script src="app.tsx"></script>
    [GeneratedRegex(@"<script src=""([^'""]+)""></script>", RegexOptions.Compiled)]
    public static partial Regex HtmlScript();

    // import("foo")
    [GeneratedRegex(@"import\(\s*(['""])([^'""]+)\1\s*\)", RegexOptions.Compiled)]
    public static partial Regex JsAsyncImport();

    // import * as utils from './utils.js'
    // import { foo } from './bar.js'
    // import './bar.js'
    [GeneratedRegex(@"import\s+((\{[^}]+\}\s+from\s+|[^'""]+\s+from\s+|\*\s+as\s+[^'""]+\s+)?(['""])([^'""]+)\3)", RegexOptions.Compiled)]
    public static partial Regex JsSyncImport();

    // export * from './module';
    // export { foo } from './module';
    // export { default } from './module';
    [GeneratedRegex(@"export\s+(?:\*\s+from|{[^}]+}\s+from)\s+(['""])([^'""]+)\1", RegexOptions.Compiled)]
    public static partial Regex JsSyncExport();

    // require("foo")
    [GeneratedRegex(@"require\(\s*(['""])([^'""]+)\1\s*\)", RegexOptions.Compiled)]
    public static partial Regex JsRequire();

    // @import url('styles.css')
    // @import 'reset.css'
    // @import url(\"https://example.com/fonts.css\")
    [GeneratedRegex(@"@import\s+(?:url\(['""]?([^'"")]+)['""]?\)|['""]([^'""]+)['""])\s*;", RegexOptions.Compiled)]
    public static partial Regex CssImport();
}
