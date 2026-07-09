namespace NetPack.Graph;

[Flags]
public enum FeatureFlags
{
    None = 0,
    // Note: 1 was TypeScript, handled natively by the parser and no longer a
    // feature flag. Kept as a gap so the remaining flag values stay stable.
    Sass = 2,
    Less = 4,
    PostCss = 8,
}
