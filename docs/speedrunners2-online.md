# SpeedRunners 2 independent Nextendo route

Application01004CC023582000, update1.2.0. Use South America and crossplay off for the verified pairing.
The title-scoped built-in DNS routes send exact hosts api.braincloudservers.com,
ns.photonengine.io and api.gameanalytics.com to the configured Nextendo address.
The native Photon backend returns its own master/game addresses; analytics is
accepted and closed internally. No public gameplay or analytics upstream is used.
Exact user hosts overrides retain their existing precedence. These rules do not
change DNS for other titles, and remain inside the existing Nextendo DNS/mode gates.

The TLS change translates expected handshake abort/reset/authentication failures to
emulated SSL results, always restoring socket mode. It does not change certificate
validation policy or swallow unexpected programming exceptions. The linked-source
regression reproduces an actual loopback TLS reset and verifies error translation.

Complete races with results on both clients were owner-confirmed using the equivalent
routes in a private1.8.11-derived candidate against Citron and a physical Switch.
That validates the candidate's networking behavior; this PR's current-main frontend
has not been fully rebuilt or tested on the physical console pairing yet.
Run dotnet run --project tests/speedrunners2/Tests.csproj -c Release for the isolated
regression checks. A clean full build and release-candidate trial remain for review.

Tournament is deferred. Friend invitations and durable ranked statistics remain pending.
Manual private-room codes were previously verified; do not infer invitation support.
