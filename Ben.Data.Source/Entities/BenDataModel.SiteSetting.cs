using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class SiteSetting : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The width of the <see cref="Description"/> column.</summary>
        /// <remarks>
        /// The stored description is only a fallback: every read replaces it with the current
        /// declaration in <c>SiteSettingKeys.Seed</c>. So a declaration longer than the column is
        /// clipped when written, rather than refusing the setting itself. The Ghost Walk switch's
        /// 560-character explanation made its first save answer 400 on a fresh database (10/21/2026).
        /// </remarks>
        public const int DescriptionMaxLength = 512;
    }
}
