namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>Provider facts Application needs without knowing the concrete provider model.</summary>
    public interface IProviderPolicy
    {
        /// <summary>True when the backend accepts only strictly alternating user/assistant turns; unknown, empty and null names give false.</summary>
        bool RequiresStrictRoleAlternation(string providerName);
    }
}
