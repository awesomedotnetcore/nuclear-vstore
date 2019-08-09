using System;

using NuClear.VStore.Descriptors;

namespace NuClear.VStore.DataContract
{
    public sealed class IdentifyableObjectRecord<TId> : IIdentifyable<TId>, IEquatable<IdentifyableObjectRecord<TId>>
        where TId : IEquatable<TId>
    {
        public IdentifyableObjectRecord(TId id, DateTime lastModified)
        {
            Id = id;
            LastModified = lastModified;
        }

        public TId Id { get; }

        public DateTime LastModified { get; }

        public override bool Equals(object obj)
        {
            if (!(obj is IdentifyableObjectRecord<TId> other))
            {
                return false;
            }

            return ReferenceEquals(this, other) || Equals(other);
        }

        public bool Equals(IdentifyableObjectRecord<TId> other) => Id.Equals(other.Id);

        public override int GetHashCode() => Id.GetHashCode();
    }
}