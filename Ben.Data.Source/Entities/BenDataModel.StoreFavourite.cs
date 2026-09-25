
namespace Ben.Data.Source.Entities
{
    /// <summary>A product a signed-in person has hearted (storefront).</summary>
    public class StoreFavourite
    {
        public Guid Id { get; set; }
        public Guid AppUserId { get; set; }
        public Guid ProductId { get; set; }
        public DateTime DateCreated { get; set; }

        public virtual AppUser AppUser { get; set; } = null!;
        public virtual StoreProduct Product { get; set; } = null!;
    }
}
