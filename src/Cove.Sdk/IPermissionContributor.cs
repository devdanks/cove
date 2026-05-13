using Cove.Core.Auth;

namespace Cove.Sdk;

/// <summary>
/// Implemented by extensions that declare permissions. Returned permission keys
/// MUST be namespaced with the extension id (e.g. "official-downloaders.download").
/// Unprefixed keys, the wildcard "*", and core namespace prefixes are silently
/// rejected by the PermissionRegistry on registration.
/// </summary>
public interface IPermissionContributor
{
    IEnumerable<PermissionDefinition> ContributePermissions();
}

/// <summary>
/// Implemented by extensions that contribute server-side content visibility
/// predicates. Predicates are pure C# and can only further restrict access —
/// the framework treats their result as <c>existing_decision AND extension_decision</c>.
/// </summary>
public interface IContentPolicyContributor
{
    IReadOnlyList<ContentPolicy> ContributePolicies();
}

/// <summary>
/// A registered content policy. <see cref="CanRead"/> returns true when the principal
/// may see the entity; <see cref="CanWrite"/>/<see cref="CanDelete"/> mirror the same
/// shape for mutations. Unset functions default to "allow".
/// </summary>
public sealed record ContentPolicy(
    string Id,
    string EntityKind,
    string Description,
    Func<ContentEvaluationContext, bool> CanRead,
    Func<ContentEvaluationContext, bool>? CanWrite = null,
    Func<ContentEvaluationContext, bool>? CanDelete = null);

/// <summary>
/// Context passed to content policy predicates. Includes the calling principal,
/// the entity ref, and a service provider for the request scope.
/// </summary>
public sealed record ContentEvaluationContext(
    CovePrincipal Principal,
    string EntityKind,
    string EntityId,
    IServiceProvider Services);
