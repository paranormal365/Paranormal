# Email confirmation: the same link every time (10/25/2026)

Branch: `fix/email-confirmation-same-link` (from develop).

**What Ben hit:** he sent himself a confirmation link. His profile showed one link and the email carried a
different one, and the email's link said "We can't find that link". Adding an address issues a link and
emails it. **Send confirmation link** then made a new token and retired the old one, and letters are posted a
few minutes after they are queued, so the first email to arrive carried the retired link.

**Fix:** `MyContactInfoController.IssueValidationAsync` reuses the token while it is still within its seven
days, and each send restarts the week. Changing the address still clears the token (`UpdateEmail`), so an old
email can't confirm a changed address. An expired token is replaced.

**Tests:** `Sending_again_sends_the_same_link_while_it_is_still_good` (fails on the old code) and
`An_expired_link_is_replaced_when_sent_again`. Help (`your-profile`), change log and the product PDF are updated.

**Deploy:** web API only. No migration, no setting.
