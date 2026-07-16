#!/bin/bash
# _new-page.sh — Helper for adding a new Project Analysis HTML page
#
# USAGE:
#   cd ProjectNotes/Project-Analysis-Html
#   bash _new-page.sh
#
# The script will:
#   1. Prompt for the new page details
#   2. Copy _template.html to the right folder
#   3. Add the new page link to ALL existing HTML files' navbars
#   4. Update _template.html navbar
#
# Run from within ProjectNotes/Project-Analysis-Html/

set -e
export PATH="/usr/bin:/bin:/usr/sbin:/sbin:$PATH"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

# ── 1. Gather input ──────────────────────────────────────────────────────────

echo ""
echo "=== Ben Project Analysis — New HTML Page ==="
echo ""
echo "Project folders:"
echo "  Ben.Data.Common"
echo "  Ben.Data.Source"
echo "  Ben.Data.WebApi"
echo "  Ben.Service.Mappings"
echo "  Ben.Service.Models"
echo "  Ben.Service.RepositoryService"
echo "  Ben.Service.RepositoryService.Tests"
echo "  Ben.Service.Security"
echo "  Ben.Web.Library"
echo "  Ben.Web.Tests"
echo "  Ben.Web.WebApp"
echo ""
read -p "Project folder (e.g. Ben.Service.Security): " PROJECT
read -p "New file name without .html (e.g. NewFeature): " FILENAME
read -p "Nav label (e.g. New Feature): " LABEL
read -p "Page title (e.g. Ben.Service.Security — New Feature): " TITLE
read -p "Breadcrumb color class (e.g. text-danger): " COLOR

OUTFILE="$PROJECT/$FILENAME.html"

if [ -f "$OUTFILE" ]; then
  echo "ERROR: $OUTFILE already exists."
  exit 1
fi

# ── 2. Copy template ─────────────────────────────────────────────────────────

cp "_template.html" "$OUTFILE"

# Replace title placeholder
sed -i '' "s|{{PAGE_TITLE}} — Ben Project Analysis|${TITLE} — Ben Project Analysis|g" "$OUTFILE"
sed -i '' "s|{{PAGE_TITLE}}|${TITLE}|g" "$OUTFILE"
sed -i '' "s|{{BREADCRUMB_COLOR}}|${COLOR}|g" "$OUTFILE"
sed -i '' "s|{{BREADCRUMB_PROJECT}}|${PROJECT}|g" "$OUTFILE"
sed -i '' "s|{{BREADCRUMB_PAGE}}|${FILENAME}|g" "$OUTFILE"

# Activate the current project's nav dropdown (add "active" class)
# Find the dropdown label matching the project and add active class
# The nav uses the last segment of the project name as the dropdown label:
# Ben.Data.Common → "Data.Common", Ben.Service.Security → "Service.Security", etc.
PROJECT_LABEL="${PROJECT#Ben.}"  # Strip leading "Ben."
sed -i '' "s|dropdown-toggle  ${COLOR}\" href=\"#\" role=\"button\" data-bs-toggle=\"dropdown\">${PROJECT_LABEL}|dropdown-toggle active ${COLOR}\" href=\"#\" role=\"button\" data-bs-toggle=\"dropdown\">${PROJECT_LABEL}|g" "$OUTFILE"

echo ""
echo "Created: $OUTFILE"

# ── 3. Add nav entry to ALL existing HTML files ──────────────────────────────

echo ""
read -p "Which existing item in the $PROJECT dropdown should the new page appear AFTER? (paste the label, e.g. 'Services'): " AFTER_LABEL

AFTER_HTML="<li><a class=\"dropdown-item\" href=\"../${PROJECT}/${AFTER_LABEL//[[:space:]]/-}.html\">${AFTER_LABEL}</a></li>"
NEW_HTML="<li><a class=\"dropdown-item\" href=\"../${PROJECT}/${FILENAME}.html\">${LABEL}</a></li>"

echo ""
echo "Adding nav entry after: $AFTER_LABEL"
echo "New entry: $NEW_HTML"
echo ""

for f in $(find . -name "*.html" ! -name "_template.html"); do
  sed -i '' "s|<li><a class=\"dropdown-item\" href=\"../${PROJECT}/${AFTER_LABEL}.html\">${AFTER_LABEL}</a></li>|<li><a class=\"dropdown-item\" href=\"../${PROJECT}/${AFTER_LABEL}.html\">${AFTER_LABEL}</a></li>\\
<li><a class=\"dropdown-item\" href=\"../${PROJECT}/${FILENAME}.html\">${LABEL}</a></li>|g" "$f"
done

# ── 4. Also update _template.html navbar ─────────────────────────────────────
# (already done by the loop above since _template.html is in the same folder)
# But re-run to catch _template.html specifically:
sed -i '' "s|<li><a class=\"dropdown-item\" href=\"../${PROJECT}/${AFTER_LABEL}.html\">${AFTER_LABEL}</a></li>|<li><a class=\"dropdown-item\" href=\"../${PROJECT}/${AFTER_LABEL}.html\">${AFTER_LABEL}</a></li>\\
<li><a class=\"dropdown-item\" href=\"../${PROJECT}/${FILENAME}.html\">${LABEL}</a></li>|g" "_template.html"

echo "Done! Nav updated in all existing HTML files."
echo ""
echo "Next steps:"
echo "  1. Open $OUTFILE and replace {{TOC_ENTRIES}} and {{PAGE_CONTENT}}"
echo "  2. Also update the companion Markdown file in ProjectNotes/Project-Analysis/${PROJECT}/"
