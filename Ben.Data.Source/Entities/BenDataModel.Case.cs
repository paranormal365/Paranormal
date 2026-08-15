using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class Case : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The physical location this case concerns, once one has been resolved.
        /// </summary>
        /// <remarks>
        /// <para>Nullable, and the case keeps its own address columns. The address on a case is the
        /// record of what the client actually reported and must not be rewritten by a shared row
        /// that a different organization may later correct. The place is the shared identity used
        /// for mapping and for "who else has been here"; the case address is the paperwork.</para>
        ///
        /// <para>Populated for existing rows by the <c>BackfillPlacesFromCases</c> migration.</para>
        /// </remarks>
        public Guid? PlaceId { get; set; }

        public virtual Place? Place { get; set; }
    }
}
