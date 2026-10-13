# Outgoing Mail test letters, and mail through MonsterASP (10/13/2026)

Ben, 10/13/2026: *"I tried to send the test email in the "Outgoing Mail" page to myself. It said it
was sent. I have not received it and it didn't add a row to the "Recent letters" list below the
test send. Could we have it generate a row there and in the test message mark that it is a test
sent from the site, but let me pick a template and it will just fill in test data in the message."*

Then: *"it should not be using smtp-auth.no-ip-com to send the e-mail any longer. I am using the
e-mail smtp from the MonsterAsp.net email hosting."* MonsterASP's settings: outgoing SMTP
`mail2205.mailasp.net`, 465 SSL/TLS, 587 STARTTLS as the alternative.

Branch `feature/mail-test-letters`, from develop.

## Why the test never arrived

`ishaunted.com`'s DNS (checked 10/13):

- MX `mail2205.mailasp.net`
- SPF `v=spf1 a mx include:spf.mailasp.net ~all`
- DMARC `p=quarantine`

The API still sent through `smtp-auth.no-ip.com` as `no-reply@ishaunted.com`. No-IP isn't in the
SPF record and doesn't sign for the domain, so DMARC fails. No-IP's server accepted the letter
(which is why the page said "sent"), and the recipient's provider then filed it as spam or dropped
it.

## Plan

1. **Mail through MonsterASP.** `appsettings.json` Smtp: Host `mail2205.mailasp.net`, Port 465,
   SslOnConnect, User and From `no-reply@ishaunted.com`. Both ports were probed from here: they
   answer, and the `*.mailasp.net` certificate is valid. The password stays out of the repo, as
   `Smtp__Password` in the API's web.config environment variables on MonsterASP.
2. **The test is recorded.** The test send still posts straight to the mail server, so it can report
   the server's own words. It then saves an outbox row that's already accepted or already given up,
   saved after the attempt so the queue never sends it again. Mail switched off is recorded too.
3. **Marked as a test.** The subject starts with `[Test]`, and a yellow box at the top says it was
   sent from the Outgoing Mail page and that the details are made up.
4. **Pick a letter.** A "Letter" choice lists every letter the site sends. A picked letter uses the
   published template, or else the ready-made starter from Email templates (the code-written
   letters can't be produced without sending something real). It's filled in with the same
   made-up rows the template preview uses (`MailSampleRows`), never real ones. The outbox kind is
   `test` or `test-<letter>`.
5. After a send, Recent letters switches to "Everything" so the new row is on top.

## Tests

- Unit: `AdminMailTestSendTests` covers:
  - the row is recorded, marked as a test, and matches what was sent;
  - a picked letter is filled in;
  - a published template is used;
  - every letter kind can be tested;
  - a refusal and mail switched off are both recorded as given up;
  - an unknown letter is refused.
- `EveryMailerGoesThroughTheOutboxTests` lists the test send as deliberately beside its context.
- Browser: `AdminMailOutboxBodyTests.A_test_send_is_listed_and_says_it_is_a_test`.

## Docs

The site-administration help (Outgoing mail) covers the Letter choice, the test marking, Recent
letters and the junk-folder/sender explanation, with a new screenshot `mail-test-send.png`. The
product PDF is rebuilt. There's no changelog line, because this is a SuperAdmin tool and the mail
host is infrastructure.

## At deploy

- Create the `no-reply@ishaunted.com` mailbox in MonsterASP email hosting if it doesn't exist.
- Set `Smtp__Password` to that mailbox's password in the API's web.config environment variables,
  replacing the No-IP one. Until the new appsettings.json is deployed, `Smtp__Host` and `Smtp__User`
  can be set the same way.
- Then send a test from Outgoing Mail.
