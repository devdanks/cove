using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Cove.Data.Repositories;

internal static class EngagementQueryHelpers
{
    public static int? CurrentUserId(CoveContext db) => db.CurrentPrincipalForReadOptimization?.UserId;

    public static IQueryable<T> ApplyRatingMinimum<T>(CoveContext db, IQueryable<T> query, int? userId, RatingHostType hostType, int minRating)
        where T : class
    {
        if (userId is not int selectedUserId)
            return query.Where(_ => false);

        return query.Where(entity => db.Ratings.Any(rating =>
            rating.UserId == selectedUserId &&
            rating.HostType == hostType &&
            rating.HostId == EF.Property<int>(entity, "Id") &&
            rating.Aspect == "overall" &&
            rating.Value >= minRating));
    }

    public static IQueryable<T> ApplyRatingCriterion<T>(CoveContext db, IQueryable<T> query, int? userId, RatingHostType hostType, IntCriterion? criterion)
        where T : class
    {
        if (criterion == null)
            return query;

        if (userId is not int selectedUserId)
            return FilterHelpers.ApplyInt(query, criterion, _ => 0);

        return FilterHelpers.ApplyInt(query, criterion, entity =>
            db.Ratings
                .Where(rating =>
                    rating.UserId == selectedUserId &&
                    rating.HostType == hostType &&
                    rating.HostId == EF.Property<int>(entity, "Id") &&
                    rating.Aspect == "overall")
                .Select(rating => rating.Value)
                .FirstOrDefault());
    }

    public static IQueryable<T> ApplyAffinityIntCriterion<T>(CoveContext db, IQueryable<T> query, int? userId, AffinityHostType hostType, string propertyName, IntCriterion? criterion)
        where T : class
    {
        if (criterion == null)
            return query;

        if (userId is not int selectedUserId)
            return FilterHelpers.ApplyInt(query, criterion, _ => 0);

        return FilterHelpers.ApplyInt(query, criterion, entity =>
            db.UserEntityAffinities
                .Where(affinity =>
                    affinity.UserId == selectedUserId &&
                    affinity.HostType == hostType &&
                    affinity.HostId == EF.Property<int>(entity, "Id"))
                .Select(affinity => EF.Property<int>(affinity, propertyName))
                .FirstOrDefault());
    }

    public static IQueryable<T> ApplyAffinityDoubleAsIntCriterion<T>(CoveContext db, IQueryable<T> query, int? userId, AffinityHostType hostType, string propertyName, IntCriterion? criterion)
        where T : class
    {
        if (criterion == null)
            return query;

        if (userId is not int selectedUserId)
            return FilterHelpers.ApplyInt(query, criterion, _ => 0);

        return FilterHelpers.ApplyInt(query, criterion, entity =>
            db.UserEntityAffinities
                .Where(affinity =>
                    affinity.UserId == selectedUserId &&
                    affinity.HostType == hostType &&
                    affinity.HostId == EF.Property<int>(entity, "Id"))
                .Select(affinity => (int)EF.Property<double>(affinity, propertyName))
                .FirstOrDefault());
    }

    public static IQueryable<T> ApplyAffinityTimestampCriterion<T>(CoveContext db, IQueryable<T> query, int? userId, AffinityHostType hostType, string propertyName, TimestampCriterion? criterion)
        where T : class
    {
        if (criterion == null)
            return query;

        if (userId is not int selectedUserId)
            return FilterHelpers.ApplyNullableTimestamp(query, criterion, _ => null);

        return FilterHelpers.ApplyNullableTimestamp(query, criterion, entity =>
            db.UserEntityAffinities
                .Where(affinity =>
                    affinity.UserId == selectedUserId &&
                    affinity.HostType == hostType &&
                    affinity.HostId == EF.Property<int>(entity, "Id"))
                .Select(affinity => EF.Property<DateTime?>(affinity, propertyName))
                .FirstOrDefault());
    }

    public static IQueryable<T> ApplyRatingSort<T>(CoveContext db, IQueryable<T> query, int? userId, RatingHostType hostType, bool desc)
        where T : class
    {
        if (userId is not int selectedUserId)
            return desc
                ? query.OrderByDescending(entity => EF.Property<int>(entity, "Id"))
                : query.OrderBy(entity => EF.Property<int>(entity, "Id"));

        var sortQuery = query.Select(entity => new
        {
            Entity = entity,
            Rating = db.Ratings
                .Where(rating =>
                    rating.UserId == selectedUserId &&
                    rating.HostType == hostType &&
                    rating.HostId == EF.Property<int>(entity, "Id") &&
                    rating.Aspect == "overall")
                .Select(rating => (int?)rating.Value)
                .FirstOrDefault(),
        });

        return desc
            ? sortQuery.OrderBy(item => item.Rating == null || item.Rating <= 0 ? 1 : 0).ThenByDescending(item => item.Rating).ThenByDescending(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity)
            : sortQuery.OrderBy(item => item.Rating == null || item.Rating <= 0 ? 0 : 1).ThenBy(item => item.Rating).ThenBy(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity);
    }

    public static IQueryable<T> ApplyAffinityIntSort<T>(CoveContext db, IQueryable<T> query, int? userId, AffinityHostType hostType, string propertyName, bool desc)
        where T : class
    {
        if (userId is not int selectedUserId)
            return desc
                ? query.OrderByDescending(entity => EF.Property<int>(entity, "Id"))
                : query.OrderBy(entity => EF.Property<int>(entity, "Id"));

        var sortQuery = query.Select(entity => new
        {
            Entity = entity,
            Value = db.UserEntityAffinities
                .Where(affinity =>
                    affinity.UserId == selectedUserId &&
                    affinity.HostType == hostType &&
                    affinity.HostId == EF.Property<int>(entity, "Id"))
                .Select(affinity => EF.Property<int>(affinity, propertyName))
                .FirstOrDefault(),
        });

        return desc
            ? sortQuery.OrderBy(item => item.Value <= 0 ? 1 : 0).ThenByDescending(item => item.Value).ThenByDescending(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity)
            : sortQuery.OrderBy(item => item.Value <= 0 ? 0 : 1).ThenBy(item => item.Value).ThenBy(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity);
    }

    public static IQueryable<T> ApplyAffinityDoubleSort<T>(CoveContext db, IQueryable<T> query, int? userId, AffinityHostType hostType, string propertyName, bool desc)
        where T : class
    {
        if (userId is not int selectedUserId)
            return desc
                ? query.OrderByDescending(entity => EF.Property<int>(entity, "Id"))
                : query.OrderBy(entity => EF.Property<int>(entity, "Id"));

        var sortQuery = query.Select(entity => new
        {
            Entity = entity,
            Value = db.UserEntityAffinities
                .Where(affinity =>
                    affinity.UserId == selectedUserId &&
                    affinity.HostType == hostType &&
                    affinity.HostId == EF.Property<int>(entity, "Id"))
                .Select(affinity => EF.Property<double>(affinity, propertyName))
                .FirstOrDefault(),
        });

        return desc
            ? sortQuery.OrderBy(item => item.Value <= 0 ? 1 : 0).ThenByDescending(item => item.Value).ThenByDescending(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity)
            : sortQuery.OrderBy(item => item.Value <= 0 ? 0 : 1).ThenBy(item => item.Value).ThenBy(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity);
    }

    public static IQueryable<T> ApplyAffinityTimestampSort<T>(CoveContext db, IQueryable<T> query, int? userId, AffinityHostType hostType, string propertyName, bool desc)
        where T : class
    {
        if (userId is not int selectedUserId)
            return desc
                ? query.OrderByDescending(entity => EF.Property<int>(entity, "Id"))
                : query.OrderBy(entity => EF.Property<int>(entity, "Id"));

        var sortQuery = query.Select(entity => new
        {
            Entity = entity,
            Value = db.UserEntityAffinities
                .Where(affinity =>
                    affinity.UserId == selectedUserId &&
                    affinity.HostType == hostType &&
                    affinity.HostId == EF.Property<int>(entity, "Id"))
                .Select(affinity => EF.Property<DateTime?>(affinity, propertyName))
                .FirstOrDefault(),
        });

        return desc
            ? sortQuery.OrderBy(item => item.Value == null ? 1 : 0).ThenByDescending(item => item.Value).ThenByDescending(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity)
            : sortQuery.OrderBy(item => item.Value == null ? 1 : 0).ThenBy(item => item.Value).ThenBy(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity);
    }

    public static IQueryable<T> ApplyInteractionTimestampSort<T>(CoveContext db, IQueryable<T> query, int? userId, InteractionHostType hostType, InteractionKind kind, bool desc)
        where T : class
    {
        if (userId is not int selectedUserId)
            return desc
                ? query.OrderByDescending(entity => EF.Property<int>(entity, "Id"))
                : query.OrderBy(entity => EF.Property<int>(entity, "Id"));

        var sortQuery = query.Select(entity => new
        {
            Entity = entity,
            Value = db.Interactions
                .Where(interaction =>
                    interaction.UserId == selectedUserId &&
                    interaction.HostType == hostType &&
                    interaction.HostId == EF.Property<int>(entity, "Id") &&
                    interaction.Kind == kind)
                .Select(interaction => (DateTime?)interaction.At)
                .Max(),
        });

        return desc
            ? sortQuery.OrderBy(item => item.Value == null ? 1 : 0).ThenByDescending(item => item.Value).ThenByDescending(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity)
            : sortQuery.OrderBy(item => item.Value == null ? 1 : 0).ThenBy(item => item.Value).ThenBy(item => EF.Property<int>(item.Entity, "Id")).Select(item => item.Entity);
    }
}
