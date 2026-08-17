using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{

    public partial class OrganizationPage : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Set on a draft, pointing at the live page it will replace. Null on every live page.
        /// </summary>
        /// <remarks>
        /// <para>A draft is a whole <see cref="OrganizationPage"/> row of its own, with its own
        /// <see cref="CmsSections"/>, rather than a flag or a parallel draft table. That is what
        /// makes the public read path need no changes at all: every existing query already filters
        /// on <c>IsPublished &amp;&amp; IsPublic</c>, and a draft is created with both false, so it
        /// is invisible to them by construction rather than by remembering to exclude it.</para>
        ///
        /// <para><b>Copy-on-write, and only for published pages.</b> A page nobody can see yet does
        /// not need a draft — editing it directly is already safe — so a draft appears the first
        /// time someone edits a page that is live. Publishing copies the draft's content onto the
        /// live row and deletes the draft, so the live page keeps its id and every link to it
        /// survives.</para>
        ///
        /// <para>Ben chose this over version history (2026-08-17), which would also have given
        /// rollback at noticeably more cost.</para>
        /// </remarks>
        public Guid? DraftOfOrganizationPageId { get; set; }

        /// <summary>The live page this draft will replace, when this row is a draft.</summary>
        public virtual OrganizationPage? DraftOfOrganizationPage { get; set; }

        /// <summary>The draft waiting to replace this page, when one has been started.</summary>
        public virtual ICollection<OrganizationPage> Drafts { get; set; } = new List<OrganizationPage>();

        /// <summary>True when this row is a draft rather than a page in its own right.</summary>
        public bool IsDraft => DraftOfOrganizationPageId is not null;
    }
}
