# Release trust anchors

Release packaging must place one or more P-256 public keys in this directory as
`<signature_key_id>.pem`. The private key is held only by release CI through
`NETACCEL_RELEASE_SIGNING_KEY_PEM` and must never be committed.

The client fails closed when the manifest key id has no matching PEM file.
