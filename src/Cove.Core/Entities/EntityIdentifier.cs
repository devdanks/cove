namespace Cove.Core.Entities;

/// <summary>
/// Schema C Stage 1: universal table replacing the per-entity URL/Alias/RemoteId family.
/// During the expand-contract migration, services dual-write to this table AND to the
/// existing typed *Url/*Alias/*RemoteId tables. A future migration will drop the typed
/// tables and migrate reads to this table.
///
/// A single UNIQUE (entity_kind, entity_id, scheme, normalized_value) index makes
/// duplicate-link bugs (the class of bug NormalizeUrlKey was added to fix) structurally
/// impossible across every entity kind.
/// </summary>
public class EntityIdentifier
{
    public int Id { get; set; }

    /// <summary>'scene' | 'performer' | 'tag' | 'studio' | 'gallery' | 'image' | 'group'</summary>
    public string EntityKind { get; set; } = string.Empty;

    public int EntityId { get; set; }

    /// <summary>'url' | 'alias' | 'remote_id'</summary>
    public string Scheme { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>Normalized form used for dedup. lowercase, host-normalized, trailing-slash-stripped for urls.</summary>
    public string NormalizedValue { get; set; } = string.Empty;

    /// <summary>For Scheme='remote_id': the source endpoint name. Null otherwise.</summary>
    public string? Source { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class EntityKinds
{
    public const string Scene = "scene";
    public const string Performer = "performer";
    public const string Face = "face";
    public const string Tag = "tag";
    public const string Studio = "studio";
    public const string Gallery = "gallery";
    public const string Image = "image";
    public const string Group = "group";
    public const string Marker = "marker";
    public const string File = "file";
}

public static class IdentifierSchemes
{
    public const string Url = "url";
    public const string Alias = "alias";
    public const string RemoteId = "remote_id";
}
