# Microsoft post-authorization Minecraft exchange

Observed failure: the Microsoft, Xbox Live and XSTS exchanges completed; the
TLS handshake for POST api.minecraftservices.com/authentication/login_with_xbox
ended with HttpRequestException/IOException before any HTTP response. The prior
implementation terminated onboarding immediately, forcing browser authorization
again after a single interrupted connection.

The Accounts service owns a bounded retry of this Minecraft session exchange
(four attempts, cancellable backoff). Each attempt creates a new request/content;
the successful Microsoft and XSTS credentials remain within the current login
operation and are neither persisted early nor reacquired. Certificate validation
failures and permanent HTTP rejection are not retried. No TLS validation, proxy,
DNS or provider authority is weakened. Persistent connectivity failures remain
visible after the retry budget; success is not fabricated.

The onboarding state stops asking for browser authorization once that step has
completed. Safe errors distinguish the Minecraft exchange/TLS connection from
HTTP rejection without forwarding provider response bodies or token contents.
Fixtures cover handshake interruption followed by success, bounded exhaustion,
certificate failure, HTTP rejection, cancellation, and no repeated OAuth/XSTS.
