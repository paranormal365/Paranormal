using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.Source.Context
{
    /// <summary>
    /// The gear store's model (branch storefront) — kept out of the 4,700-line main context.
    /// </summary>
    /// <remarks>
    /// <para><b>The rules this block keeps</b> (storefront plan §5.2): money is
    /// <c>decimal(18,2)</c>; every key to <c>AppUsers</c> the store adds is NoAction, because a
    /// person's deletion is decided by the purge, not by the database; every key to
    /// <c>UploadFiles</c> is NoAction; child rows cascade from ONE root (SQL Server refuses a second
    /// cascade path, and the second path is always the one somebody adds later); no rowversion —
    /// every race is settled by a one-statement conditional UPDATE, which behaves the same on SQL
    /// Server and in <c>SqliteTestDb</c>; CHECK constraints guard what several doors write.</para>
    ///
    /// <para><c>StoreModelGuardTests</c> walks this model and fails on a precision, a cascade path
    /// or an AppUsers key that breaks those rules.</para>
    /// </remarks>
    public partial class BenDataContext
    {
        private static void ConfigureStore(ModelBuilder modelBuilder)
        {
            ConfigureStoreCatalogue(modelBuilder);
            ConfigureStoreOrders(modelBuilder);
            ConfigureStoreEngagement(modelBuilder);
        }

        /// <summary>The two audit keys every audited store table carries, NoAction both.</summary>
        private static void StoreAudit<T>(ModelBuilder modelBuilder) where T : class, IAuditableEntity
        {
            modelBuilder.Entity<T>()
                .HasOne<AppUser>("CreatedByAppUser").WithMany()
                .HasForeignKey(nameof(IAuditableEntity.CreatedByAppUserId)).OnDelete(DeleteBehavior.NoAction);
            modelBuilder.Entity<T>()
                .HasOne<AppUser>("UpdatedByAppUser").WithMany()
                .HasForeignKey(nameof(IAuditableEntity.UpdatedByAppUserId)).IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
        }

        // ── M1: the catalogue ────────────────────────────────────────────────────────
        private static void ConfigureStoreCatalogue(ModelBuilder modelBuilder)
        {
            // Categories. Products do NOT cascade from a category: deleting a shelf must never take
            // the products (and so their order history) with it — the admin moves them first.
            var category = modelBuilder.Entity<StoreCategory>();
            category.Property(e => e.Name).HasMaxLength(100);
            category.Property(e => e.Slug).HasMaxLength(120);
            category.Property(e => e.Description).HasMaxLength(1000);
            category.HasIndex(e => e.Slug).IsUnique();
            category.HasIndex(e => new { e.IsActive, e.SortOrder });
            category.HasOne(e => e.ImageUploadFile).WithMany()
                .HasForeignKey(e => e.ImageUploadFileId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // A parent is never deleted from under its subcategories: the admin refuses with a
            // sentence first, and the database refuses too (NoAction, no cascade).
            category.HasOne(e => e.ParentCategory).WithMany(e => e.Subcategories)
                .HasForeignKey(e => e.ParentCategoryId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            category.HasIndex(e => e.ParentCategoryId);
            StoreAudit<StoreCategory>(modelBuilder);

            // Products: the root every catalogue child cascades from.
            var product = modelBuilder.Entity<StoreProduct>();
            product.Property(e => e.Name).HasMaxLength(200);
            product.Property(e => e.Slug).HasMaxLength(220);
            product.Property(e => e.ShortDescription).HasMaxLength(500);
            product.Property(e => e.StripeTaxCode).HasMaxLength(32);
            product.Property(e => e.MinPrice).HasPrecision(18, 2);
            product.Property(e => e.MaxPrice).HasPrecision(18, 2);
            product.Property(e => e.SellerAskPerUnit).HasPrecision(18, 2);
            product.Property(e => e.OtherCostPerUnit).HasPrecision(18, 4);
            product.Property(e => e.OtherCostNote).HasMaxLength(300);
            product.Property(e => e.AverageRating).HasPrecision(3, 2);
            product.HasIndex(e => e.Slug).IsUnique();
            // The listing's four questions: a shelf in order, most popular, newest, cheapest.
            product.HasIndex(e => new { e.CategoryId, e.IsActive, e.SortOrder });
            product.HasIndex(e => new { e.IsActive, e.UnitsSold });
            product.HasIndex(e => new { e.IsActive, e.DateCreated });
            product.HasIndex(e => new { e.IsActive, e.MinPrice });
            product.HasOne(e => e.Category).WithMany(c => c.Products)
                .HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.NoAction);
            product.HasOne(e => e.EquipmentModel).WithMany()
                .HasForeignKey(e => e.EquipmentModelId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // The seller. NoAction like every FK to AppUsers; deleting the person clears it
            // (AppUserPurge), so the item stays, as the site's own stock.
            product.HasIndex(e => e.SellerAppUserId);
            product.HasOne(e => e.SellerAppUser).WithMany()
                .HasForeignKey(e => e.SellerAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            StoreAudit<StoreProduct>(modelBuilder);

            var option = modelBuilder.Entity<StoreProductOption>();
            option.Property(e => e.Name).HasMaxLength(60);
            option.HasIndex(e => new { e.ProductId, e.Name }).IsUnique();
            option.HasOne(e => e.Product).WithMany(p => p.Options)
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            StoreAudit<StoreProductOption>(modelBuilder);

            var value = modelBuilder.Entity<StoreProductOptionValue>();
            value.Property(e => e.Value).HasMaxLength(60);
            value.Property(e => e.SwatchHex).HasMaxLength(7);
            value.HasIndex(e => new { e.OptionId, e.Value }).IsUnique();
            value.HasOne(e => e.Option).WithMany(o => o.Values)
                .HasForeignKey(e => e.OptionId).OnDelete(DeleteBehavior.Cascade);
            StoreAudit<StoreProductOptionValue>(modelBuilder);

            // Variants: what is priced, stocked and ordered. The stock CHECK is the database half
            // of the rule every conditional UPDATE in StoreStock keeps: nothing can hold more than
            // is on the shelf, and nothing goes negative.
            var variant = modelBuilder.Entity<StoreProductVariant>();
            variant.Property(e => e.Sku).HasMaxLength(64);
            variant.Property(e => e.Name).HasMaxLength(200);
            variant.Property(e => e.OptionSignature).HasMaxLength(200);
            variant.Property(e => e.Price).HasPrecision(18, 2);
            variant.Property(e => e.CompareAtPrice).HasPrecision(18, 2);
            variant.HasIndex(e => e.Sku).IsUnique();
            variant.HasIndex(e => new { e.ProductId, e.OptionSignature }).IsUnique();
            variant.HasOne(e => e.Product).WithMany(p => p.Variants)
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            variant.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StoreProductVariants_Price", "[Price] >= 0");
                t.HasCheckConstraint("CK_StoreProductVariants_CompareAtPrice",
                    "[CompareAtPrice] IS NULL OR [CompareAtPrice] > [Price]");
                t.HasCheckConstraint("CK_StoreProductVariants_Stock",
                    "[StockOnHand] >= 0 AND [StockReserved] >= 0 AND [StockReserved] <= [StockOnHand]");
            });
            StoreAudit<StoreProductVariant>(modelBuilder);

            // A variant's choices. Cascade from the variant; NoAction to the value — the value
            // already cascades from the product through its option, and a second path is refused.
            var variantValue = modelBuilder.Entity<StoreProductVariantOptionValue>();
            variantValue.HasIndex(e => new { e.VariantId, e.OptionValueId }).IsUnique();
            variantValue.HasOne(e => e.Variant).WithMany(v => v.OptionValues)
                .HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Cascade);
            variantValue.HasOne(e => e.OptionValue).WithMany()
                .HasForeignKey(e => e.OptionValueId).OnDelete(DeleteBehavior.NoAction);

            // Pictures. Variant is NoAction (the app nulls it before a variant goes): the image
            // already cascades from the product.
            var image = modelBuilder.Entity<StoreProductImage>();
            image.Property(e => e.AltText).HasMaxLength(200);
            image.HasIndex(e => new { e.ProductId, e.UploadFileId }).IsUnique();
            image.HasOne(e => e.Product).WithMany(p => p.Images)
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            image.HasOne(e => e.Variant).WithMany()
                .HasForeignKey(e => e.VariantId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            image.HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).OnDelete(DeleteBehavior.NoAction);
            StoreAudit<StoreProductImage>(modelBuilder);

            var spec = modelBuilder.Entity<StoreProductSpec>();
            spec.Property(e => e.GroupName).HasMaxLength(100);
            spec.Property(e => e.Name).HasMaxLength(100);
            spec.Property(e => e.Value).HasMaxLength(500);
            spec.HasOne(e => e.Product).WithMany(p => p.Specs)
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            StoreAudit<StoreProductSpec>(modelBuilder);

            var coupon = modelBuilder.Entity<StoreCoupon>();
            coupon.Property(e => e.Code).HasMaxLength(64);
            coupon.Property(e => e.Name).HasMaxLength(150);
            coupon.Property(e => e.AmountOff).HasPrecision(18, 2);
            coupon.Property(e => e.MinimumOrderAmount).HasPrecision(18, 2);
            coupon.HasIndex(e => e.Code).IsUnique();
            coupon.ToTable(t => t.HasCheckConstraint("CK_StoreCoupons_PercentOff",
                "[PercentOff] IS NULL OR ([PercentOff] >= 1 AND [PercentOff] <= 100)"));
            StoreAudit<StoreCoupon>(modelBuilder);
        }

        // ── M2: carts, orders and the money around them ─────────────────────────────────
        private static void ConfigureStoreOrders(ModelBuilder modelBuilder)
        {
            // Carts. One per account, one per browser token; never neither. The applied coupon is
            // SetNull: retiring a code must not delete the carts that had it applied.
            var cart = modelBuilder.Entity<StoreCart>();
            cart.Property(e => e.GuestTokenHash).HasMaxLength(64);
            cart.HasIndex(e => e.AppUserId).IsUnique().HasFilter("[AppUserId] IS NOT NULL");
            cart.HasIndex(e => e.GuestTokenHash).IsUnique().HasFilter("[GuestTokenHash] IS NOT NULL");
            cart.HasIndex(e => e.LastActivityUtc);
            cart.HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            cart.HasOne(e => e.Coupon).WithMany()
                .HasForeignKey(e => e.CouponId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            cart.ToTable(t => t.HasCheckConstraint("CK_StoreCarts_Identity",
                "[AppUserId] IS NOT NULL OR [GuestTokenHash] IS NOT NULL"));

            var cartItem = modelBuilder.Entity<StoreCartItem>();
            cartItem.HasIndex(e => new { e.CartId, e.VariantId }).IsUnique();
            cartItem.HasOne(e => e.Cart).WithMany(c => c.Items)
                .HasForeignKey(e => e.CartId).OnDelete(DeleteBehavior.Cascade);
            cartItem.HasOne(e => e.Variant).WithMany()
                .HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Cascade);
            cartItem.ToTable(t => t.HasCheckConstraint("CK_StoreCartItems_Quantity",
                "[Quantity] >= 1 AND [Quantity] <= 100"));

            // Orders: the money record. Nothing that points at a person or a product deletes one.
            var order = modelBuilder.Entity<StoreOrder>();
            order.Property(e => e.BuyerEmail).HasMaxLength(256);
            order.Property(e => e.BuyerEmailNormalized).HasMaxLength(256);
            order.Property(e => e.BuyerName).HasMaxLength(150);
            order.Property(e => e.ShipName).HasMaxLength(150);
            order.Property(e => e.ShipPhone).HasMaxLength(40);
            order.Property(e => e.ShipStreet1).HasMaxLength(200);
            order.Property(e => e.ShipStreet2).HasMaxLength(200);
            order.Property(e => e.ShipCity).HasMaxLength(100);
            order.Property(e => e.ShipState).HasMaxLength(2);
            order.Property(e => e.ShipZip).HasMaxLength(10);
            order.Property(e => e.ShipCountry).HasMaxLength(2);
            order.Property(e => e.BillName).HasMaxLength(150);
            order.Property(e => e.BillCompany).HasMaxLength(150);
            order.Property(e => e.BillStreet1).HasMaxLength(200);
            order.Property(e => e.BillStreet2).HasMaxLength(200);
            order.Property(e => e.BillCity).HasMaxLength(100);
            order.Property(e => e.BillState).HasMaxLength(2);
            order.Property(e => e.BillZip).HasMaxLength(10);
            order.Property(e => e.BillCountry).HasMaxLength(2);
            order.Property(e => e.Subtotal).HasPrecision(18, 2);
            order.Property(e => e.DiscountAmount).HasPrecision(18, 2);
            order.Property(e => e.ShippingAmount).HasPrecision(18, 2);
            order.Property(e => e.TaxAmount).HasPrecision(18, 2);
            order.Property(e => e.ShippingTaxAmount).HasPrecision(18, 2);
            order.Property(e => e.Total).HasPrecision(18, 2);
            order.Property(e => e.RefundedAmount).HasPrecision(18, 2);
            order.Property(e => e.Currency).HasMaxLength(3);
            order.Property(e => e.CouponCode).HasMaxLength(64);
            order.Property(e => e.CartFingerprint).HasMaxLength(64);
            order.Property(e => e.PlacedFromIp).HasMaxLength(45);
            order.Property(e => e.StripePaymentIntentId).HasMaxLength(128);
            order.Property(e => e.StripeChargeId).HasMaxLength(128);
            order.Property(e => e.StripeTaxCalculationId).HasMaxLength(128);
            order.Property(e => e.StripeTaxTransactionId).HasMaxLength(128);
            order.Property(e => e.AccessToken).HasMaxLength(64);
            order.Property(e => e.CancellationReason).HasMaxLength(500);
            order.Property(e => e.BuyerNotes).HasMaxLength(1000);
            order.Property(e => e.AdminNotes).HasMaxLength(2000);
            order.Property(e => e.AttentionReason).HasMaxLength(300);
            order.Property(e => e.StripeFeeAmount).HasPrecision(18, 2);
            order.Property(e => e.StripeNetAmount).HasPrecision(18, 2);
            order.HasIndex(e => e.OrderNumber).IsUnique();
            // The webhook's idempotency key and the order's identity at Stripe.
            order.HasIndex(e => e.StripePaymentIntentId).IsUnique().HasFilter("[StripePaymentIntentId] IS NOT NULL");
            order.HasIndex(e => e.AccessToken).IsUnique();
            order.HasIndex(e => new { e.Status, e.PlacedUtc });
            order.HasIndex(e => new { e.BuyerAppUserId, e.PlacedUtc });
            order.HasIndex(e => e.BuyerEmail);
            order.HasIndex(e => e.BuyerEmailNormalized);
            order.HasIndex(e => new { e.StoreCartId, e.Status });
            order.HasIndex(e => e.NeedsAttention).HasFilter("[NeedsAttention] = 1");
            // The open-checkout cap: open (PendingPayment = 0) orders from one address.
            order.HasIndex(e => new { e.Status, e.PlacedFromIp }).HasFilter("[Status] = 0");
            order.HasOne(e => e.Cart).WithMany()
                .HasForeignKey(e => e.StoreCartId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            order.HasOne(e => e.BuyerAppUser).WithMany()
                .HasForeignKey(e => e.BuyerAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            order.HasOne(e => e.Coupon).WithMany()
                .HasForeignKey(e => e.CouponId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            order.ToTable(t => t.HasCheckConstraint("CK_StoreOrders_Total", "[Total] >= 0"));

            // Order lines. The product and variant keys are NoAction on purpose: a product that
            // has been sold cannot be deleted, only deactivated.
            var item = modelBuilder.Entity<StoreOrderItem>();
            item.Property(e => e.ProductName).HasMaxLength(200);
            item.Property(e => e.VariantName).HasMaxLength(200);
            item.Property(e => e.Sku).HasMaxLength(64);
            item.Property(e => e.UnitPrice).HasPrecision(18, 2);
            item.Property(e => e.CompareAtPrice).HasPrecision(18, 2);
            item.Property(e => e.LineTotal).HasPrecision(18, 2);
            item.Property(e => e.LineDiscount).HasPrecision(18, 2);
            item.Property(e => e.TaxAmount).HasPrecision(18, 2);
            item.Property(e => e.StripeTaxCode).HasMaxLength(32);
            item.Property(e => e.UnitCostBasis).HasPrecision(18, 2);
            item.Property(e => e.UnitSellerAsk).HasPrecision(18, 2);
            item.Property(e => e.UnitSellerEarning).HasPrecision(18, 2);
            item.Property(e => e.UnitSiteMarkup).HasPrecision(18, 2);
            item.HasIndex(e => e.OrderId);
            item.HasIndex(e => e.VariantId);
            item.HasIndex(e => new { e.ProductId, e.OrderId });
            item.HasOne(e => e.Order).WithMany(o => o.Items)
                .HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
            item.HasOne(e => e.Product).WithMany()
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.NoAction);
            item.HasOne(e => e.Variant).WithMany()
                .HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.NoAction);
            item.HasOne(e => e.ImageUploadFile).WithMany()
                .HasForeignKey(e => e.ImageUploadFileId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            item.ToTable(t => t.HasCheckConstraint("CK_StoreOrderItems_Quantity", "[Quantity] >= 1"));

            // Store sellers P5: an order's packages, one per seller. The seller's key is NoAction —
            // an account's closure anonymises the snapshot rather than losing the package.
            var parcel = modelBuilder.Entity<StoreOrderParcel>();
            parcel.Property(e => e.SellerName).HasMaxLength(200);
            parcel.Property(e => e.ShippingAmount).HasPrecision(18, 2);
            parcel.Property(e => e.ShippingTaxAmount).HasPrecision(18, 2);
            parcel.Property(e => e.ShippingRefunded).HasPrecision(18, 2);
            parcel.Property(e => e.SellerShippingCredit).HasPrecision(18, 2);
            parcel.Property(e => e.Carrier).HasMaxLength(60);
            parcel.Property(e => e.TrackingNumber).HasMaxLength(100);
            parcel.Property(e => e.TrackingUrl).HasMaxLength(500);
            parcel.HasIndex(e => new { e.OrderId, e.Number }).IsUnique();
            parcel.HasIndex(e => new { e.SellerAppUserId, e.Status });
            parcel.HasOne(e => e.Order).WithMany(o => o.Parcels)
                .HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
            parcel.HasOne(e => e.SellerAppUser).WithMany()
                .HasForeignKey(e => e.SellerAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            parcel.ToTable(t => t.HasCheckConstraint("CK_StoreOrderParcels_Refund", "[ShippingRefunded] >= 0 AND [ShippingRefunded] <= [ShippingAmount] + [ShippingTaxAmount]"));
            item.HasOne(e => e.Parcel).WithMany(p => p.Items)
                .HasForeignKey(e => e.ParcelId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            var orderEvent = modelBuilder.Entity<StoreOrderEvent>();
            orderEvent.Property(e => e.Note).HasMaxLength(1000);
            orderEvent.Property(e => e.Amount).HasPrecision(18, 2);
            orderEvent.HasIndex(e => new { e.OrderId, e.OccurredUtc });
            orderEvent.HasOne(e => e.Order).WithMany(o => o.Events)
                .HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
            orderEvent.HasOne(e => e.ActorAppUser).WithMany()
                .HasForeignKey(e => e.ActorAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            orderEvent.HasOne<StoreOrderParcel>().WithMany()
                .HasForeignKey(e => e.ParcelId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // Refunds. StripeRefundId is unique: a redelivered refund event finds its row instead of
            // inserting a second one.
            var refund = modelBuilder.Entity<StoreRefund>();
            refund.Property(e => e.Amount).HasPrecision(18, 2);
            refund.Property(e => e.TaxReversed).HasPrecision(18, 2);
            refund.Property(e => e.Reason).HasMaxLength(500);
            refund.Property(e => e.StripeRefundId).HasMaxLength(128);
            refund.Property(e => e.StripeTaxReversalId).HasMaxLength(128);
            refund.Property(e => e.FailureReason).HasMaxLength(1000);
            refund.HasIndex(e => e.StripeRefundId).IsUnique().HasFilter("[StripeRefundId] IS NOT NULL");
            refund.HasOne(e => e.Order).WithMany(o => o.Refunds)
                .HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
            refund.HasOne(e => e.RequestedByAppUser).WithMany()
                .HasForeignKey(e => e.RequestedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            refund.ToTable(t => t.HasCheckConstraint("CK_StoreRefunds_Amount", "[Amount] > 0"));

            // A refund line cascades from its refund; the order line it points at already cascades
            // from the order, so that key is NoAction.
            var refundItem = modelBuilder.Entity<StoreRefundItem>();
            refundItem.Property(e => e.Amount).HasPrecision(18, 2);
            refundItem.HasIndex(e => new { e.RefundId, e.OrderItemId }).IsUnique();
            refundItem.HasOne(e => e.Refund).WithMany(r => r.Items)
                .HasForeignKey(e => e.RefundId).OnDelete(DeleteBehavior.Cascade);
            refundItem.HasOne(e => e.OrderItem).WithMany()
                .HasForeignKey(e => e.OrderItemId).OnDelete(DeleteBehavior.NoAction);
            refundItem.ToTable(t => t.HasCheckConstraint("CK_StoreRefundItems_Quantity", "[Quantity] >= 1"));

            // Store sellers P11: files that go with a product. The bytes are NoAction — the editor
            // removes them with the row.
            var productFile = modelBuilder.Entity<StoreProductFile>();
            productFile.Property(e => e.Title).HasMaxLength(StoreProductFile.MaxTitleLength);
            productFile.Property(e => e.VersionLabel).HasMaxLength(StoreProductFile.MaxVersionLength);
            productFile.Property(e => e.FileName).HasMaxLength(260);
            productFile.HasIndex(e => new { e.ProductId, e.SortOrder });
            productFile.HasOne(e => e.Product).WithMany()
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            productFile.HasOne(e => e.UploadFile).WithMany()
                .HasForeignKey(e => e.UploadFileId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // Store sellers P12: a product's FAQ, and shoppers' questions. The asker's key is NoAction —
            // closing or purging the account removes their questions first (AccountClosureService).
            // FaqEnabled starts true for existing products: the migration's column default says so.
            var faq = modelBuilder.Entity<StoreProductFaq>();
            faq.Property(e => e.Question).HasMaxLength(StoreProductFaq.MaxQuestionLength);
            faq.Property(e => e.Answer).HasMaxLength(StoreProductFaq.MaxAnswerLength);
            faq.HasIndex(e => new { e.ProductId, e.SortOrder });
            faq.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            var question = modelBuilder.Entity<StoreProductQuestion>();
            question.Property(e => e.Question).HasMaxLength(StoreProductQuestion.MaxQuestionLength);
            question.Property(e => e.Answer).HasMaxLength(StoreProductQuestion.MaxAnswerLength);
            question.HasIndex(e => new { e.ProductId, e.Status });
            question.HasIndex(e => new { e.AskerAppUserId, e.DateCreated });
            question.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            question.HasOne(e => e.AskerAppUser).WithMany().HasForeignKey(e => e.AskerAppUserId).OnDelete(DeleteBehavior.NoAction);

            // Store sellers P13: versions. One newer version per product; NoAction — a product with a
            // newer version is never deleted (it has been sold, or the editor refuses).
            var versioned = modelBuilder.Entity<StoreProduct>();
            versioned.Property(e => e.VersionLabel).HasMaxLength(40);
            versioned.HasIndex(e => e.PreviousVersionProductId).IsUnique().HasFilter("[PreviousVersionProductId] IS NOT NULL");
            versioned.HasOne(e => e.PreviousVersion).WithMany()
                .HasForeignKey(e => e.PreviousVersionProductId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // Store sellers P10: what sellers are owed, and what they've been paid. Every person key is
            // NoAction: these are money records, kept when an account goes (its name is anonymised).
            var earning = modelBuilder.Entity<StoreSellerEarning>();
            earning.Property(e => e.Amount).HasPrecision(18, 2);
            earning.Property(e => e.Note).HasMaxLength(StoreSellerPayout.MaxNoteLength);
            earning.HasIndex(e => new { e.SellerAppUserId, e.PayoutId, e.OccurredUtc });
            earning.HasIndex(e => e.OrderItemId).IsUnique().HasFilter("[Kind] = 0");
            earning.HasIndex(e => e.ParcelId).IsUnique().HasFilter("[Kind] = 1");
            earning.HasIndex(e => new { e.RefundId, e.OrderItemId }).IsUnique().HasFilter("[Kind] = 2");
            earning.HasOne(e => e.SellerAppUser).WithMany()
                .HasForeignKey(e => e.SellerAppUserId).OnDelete(DeleteBehavior.NoAction);
            earning.HasOne(e => e.Payout).WithMany()
                .HasForeignKey(e => e.PayoutId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            earning.HasOne<AppUser>().WithMany()
                .HasForeignKey(e => e.CreatedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            var payout = modelBuilder.Entity<StoreSellerPayout>();
            payout.Property(e => e.Amount).HasPrecision(18, 2);
            payout.Property(e => e.Reference).HasMaxLength(StoreSellerPayout.MaxNoteLength);
            payout.Property(e => e.VoidReason).HasMaxLength(StoreSellerPayout.MaxNoteLength);
            payout.HasIndex(e => new { e.SellerAppUserId, e.PaidOnUtc });
            payout.HasOne(e => e.SellerAppUser).WithMany()
                .HasForeignKey(e => e.SellerAppUserId).OnDelete(DeleteBehavior.NoAction);
            payout.HasOne<AppUser>().WithMany()
                .HasForeignKey(e => e.RecordedByAppUserId).OnDelete(DeleteBehavior.NoAction);
            payout.HasOne<AppUser>().WithMany()
                .HasForeignKey(e => e.VoidedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // Store sellers P8: package shipping given back with a refund.
            var refundShipping = modelBuilder.Entity<StoreRefundShipping>();
            refundShipping.Property(e => e.Amount).HasPrecision(18, 2);
            refundShipping.HasIndex(e => new { e.RefundId, e.ParcelId }).IsUnique();
            refundShipping.HasIndex(e => e.ParcelId);
            refundShipping.HasOne(e => e.Refund).WithMany(r => r.Shipping)
                .HasForeignKey(e => e.RefundId).OnDelete(DeleteBehavior.Cascade);
            refundShipping.HasOne(e => e.Parcel).WithMany()
                .HasForeignKey(e => e.ParcelId).OnDelete(DeleteBehavior.NoAction);
            refundShipping.ToTable(t => t.HasCheckConstraint("CK_StoreRefundShipping_Amount", "[Amount] > 0"));

            // One redemption per order. Restrict to the coupon — a financial record, like the
            // subscription CouponRedemption.
            var redemption = modelBuilder.Entity<StoreCouponRedemption>();
            redemption.Property(e => e.BuyerEmailNormalized).HasMaxLength(256);
            redemption.Property(e => e.DiscountAmount).HasPrecision(18, 2);
            redemption.HasIndex(e => e.OrderId).IsUnique();
            redemption.HasIndex(e => new { e.CouponId, e.BuyerEmailNormalized });
            redemption.HasIndex(e => new { e.CouponId, e.BuyerAppUserId });
            redemption.HasOne(e => e.Coupon).WithMany()
                .HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Restrict);
            redemption.HasOne(e => e.Order).WithMany()
                .HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
            redemption.HasOne(e => e.BuyerAppUser).WithMany()
                .HasForeignKey(e => e.BuyerAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // The stock ledger. Cascade from the variant only; the order and refund it names are
            // NoAction (they already have their own roots).
            var movement = modelBuilder.Entity<StoreStockMovement>();
            movement.Property(e => e.Note).HasMaxLength(300);
            movement.HasIndex(e => new { e.VariantId, e.OccurredUtc });
            movement.HasOne(e => e.Variant).WithMany()
                .HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Cascade);
            movement.HasOne(e => e.Order).WithMany()
                .HasForeignKey(e => e.OrderId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            movement.HasOne(e => e.Refund).WithMany()
                .HasForeignKey(e => e.RefundId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            movement.HasOne(e => e.ActorAppUser).WithMany()
                .HasForeignKey(e => e.ActorAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            movement.ToTable(t => t.HasCheckConstraint("CK_StoreStockMovements_Delta", "[Delta] <> 0"));

            // Store sellers P2: an item's history. It goes with the item (only a never-sold item
            // can be deleted); the person who made a change is NoAction, like the stock log's.
            var change = modelBuilder.Entity<StoreProductChange>();
            change.Property(e => e.Summary).HasMaxLength(StoreProductChange.MaxSummaryLength);
            change.HasIndex(e => new { e.ProductId, e.OccurredUtc });
            change.HasOne(e => e.Product).WithMany()
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            change.HasOne(e => e.ActorAppUser).WithMany()
                .HasForeignKey(e => e.ActorAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);

            // Store sellers P3: a seller asking for an item to go on sale. One open request per
            // item, held by a filtered unique index rather than by a check that two clicks can race.
            var request = modelBuilder.Entity<StoreProductSaleRequest>();
            request.Property(e => e.SellerAskingPrice).HasPrecision(18, 2);
            request.Property(e => e.SellerNote).HasMaxLength(StoreProductSaleRequest.MaxNoteLength);
            request.Property(e => e.DecisionNote).HasMaxLength(StoreProductSaleRequest.MaxNoteLength);
            request.HasIndex(e => e.ProductId).IsUnique().HasFilter("[Status] = 0");
            request.HasIndex(e => new { e.Status, e.RequestedUtc });
            request.HasOne(e => e.Product).WithMany()
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            request.HasOne(e => e.SellerAppUser).WithMany()
                .HasForeignKey(e => e.SellerAppUserId).OnDelete(DeleteBehavior.NoAction);
            request.HasOne(e => e.DecidedByAppUser).WithMany()
                .HasForeignKey(e => e.DecidedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            request.ToTable(t => t.HasCheckConstraint("CK_StoreProductSaleRequests_Ask", "[SellerAskingPrice] > 0"));

            // Store sellers P4: what an item is built from. Goes with the item.
            var part = modelBuilder.Entity<StoreProductPart>();
            part.Property(e => e.Name).HasMaxLength(StoreProductPart.MaxNameLength);
            part.Property(e => e.InfoUrl).HasMaxLength(StoreProductPart.MaxUrlLength);
            part.Property(e => e.BuyUrl).HasMaxLength(StoreProductPart.MaxUrlLength);
            part.Property(e => e.Price).HasPrecision(18, 4);
            part.Property(e => e.QuantityPerUnit).HasPrecision(18, 4);
            part.HasIndex(e => new { e.ProductId, e.SortOrder });
            part.HasOne(e => e.Product).WithMany()
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            part.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StoreProductParts_Price", "[Price] >= 0");
                t.HasCheckConstraint("CK_StoreProductParts_Pack", "[PiecesPerPack] >= 1");
                t.HasCheckConstraint("CK_StoreProductParts_PerUnit", "[QuantityPerUnit] >= 0");
            });
        }

        // ── M3: favourites, reviews and helpful votes ─────────────────────────────────────
        // Every person key is NoAction: AppUserPurge sweeps favourites and votes itself, and a
        // review outlives its author's account (anonymised, like every other authored table).
        private static void ConfigureStoreEngagement(ModelBuilder modelBuilder)
        {
            var favourite = modelBuilder.Entity<StoreFavourite>();
            favourite.HasIndex(e => new { e.AppUserId, e.ProductId }).IsUnique();
            favourite.HasIndex(e => e.ProductId);
            favourite.HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
            favourite.HasOne(e => e.Product).WithMany()
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);

            // A review cascades from its product only; the order it proves was bought is NoAction
            // (orders have their own root, and an order is never deleted while a review cites it).
            var review = modelBuilder.Entity<StoreReview>();
            review.Property(e => e.Title).HasMaxLength(150);
            review.Property(e => e.Body).HasMaxLength(3000);
            review.Property(e => e.RejectionReason).HasMaxLength(500);
            review.Property(e => e.AdminReply).HasMaxLength(1000);
            review.HasIndex(e => new { e.ProductId, e.AuthorAppUserId }).IsUnique();
            review.HasIndex(e => new { e.ProductId, e.Status, e.DateCreated });
            review.HasIndex(e => e.Status);
            review.HasIndex(e => new { e.ProductId, e.Status, e.HelpfulCount });
            review.HasOne(e => e.Product).WithMany()
                .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            review.HasOne(e => e.AuthorAppUser).WithMany()
                .HasForeignKey(e => e.AuthorAppUserId).OnDelete(DeleteBehavior.NoAction);
            review.HasOne(e => e.Order).WithMany()
                .HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.NoAction);
            review.HasOne(e => e.ModeratedByAppUser).WithMany()
                .HasForeignKey(e => e.ModeratedByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            review.HasOne(e => e.AdminReplyByAppUser).WithMany()
                .HasForeignKey(e => e.AdminReplyByAppUserId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            review.ToTable(t => t.HasCheckConstraint("CK_StoreReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5"));
            StoreAudit<StoreReview>(modelBuilder);

            var vote = modelBuilder.Entity<StoreReviewVote>();
            vote.HasIndex(e => new { e.ReviewId, e.AppUserId }).IsUnique();
            vote.HasOne(e => e.Review).WithMany()
                .HasForeignKey(e => e.ReviewId).OnDelete(DeleteBehavior.Cascade);
            vote.HasOne(e => e.AppUser).WithMany()
                .HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.NoAction);
        }
    }
}
