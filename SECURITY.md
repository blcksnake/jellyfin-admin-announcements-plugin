# Security Policy

## Supported Versions

- Latest release branch is supported for security fixes.

## Reporting a Vulnerability

Please do not open public issues for security vulnerabilities.

Report privately by contacting the maintainer and include:

- Affected version
- Impact summary
- Reproduction details
- Suggested remediation, if known

You will receive an acknowledgement as soon as possible, followed by triage and a fix timeline.

## Security boundaries in version 0.2.0.8

- The public feed checks verified Jellyfin identity, audience, roles, specific users, and library access before returning display data. Only explicitly enabled public login announcements are returned anonymously. Device targeting is a presentation preference.
- Analytics are unverified browser reports. They are not suitable for billing, auditing, or proof of viewing. Atomic throttling, bounded state, and batched writes limit resource use; they do not authenticate a human action.
- Web patch locations are authorized by the local deployment, not by arbitrary paths submitted through the admin page. Standard locations and local environment overrides are supported. Symlinks are rejected. The filesystem and deployment configuration must be controlled by trusted OS operators; this does not defend against a local attacker who can replace deployment directories during a write.
- If the store cannot be read, writes remain disabled until repair and restart. Malformed JSON is copied to a unique recovery file before a fresh store can be saved. Recovery files may contain private announcement text and need the same access protection as the main store.
