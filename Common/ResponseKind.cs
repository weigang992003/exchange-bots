

namespace Common
{
    public enum ResponseKind
    {
        Success,
        /// <summary>Response denotes error that can be treated without stopping trading</summary>
        Error,
        /// <summary>Response denotes error that should stop trading</summary>
        FatalError
    }
}
