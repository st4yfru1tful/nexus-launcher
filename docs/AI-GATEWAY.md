# Nexus AI gateway deployment contract

## Why this gateway exists

Nexus Launcher does not support a direct **Sign in with OpenAI** flow. The
desktop client intentionally contains no OpenAI API key, OpenAI user token, or
OAuth client secret. Its OAuth implementation is for a **developer-owned Nexus
AI gateway** that can apply user consent, quotas, abuse controls, and a
server-side model integration.

The public v0.2.0 build contains the desktop client only. It does not ship a
Nexus identity service or gateway, so AI stays unavailable until a maintainer
deploys those services.

## Desktop configuration

The following are public OAuth/deployment values, not secrets. They are read
from the process environment to keep an ordinary release unconfigured by
default:

| Variable | Purpose |
| --- | --- |
| NEXUS_AI_GATEWAY_URL | Base HTTPS URL of the Nexus AI API |
| NEXUS_AI_OAUTH_AUTHORIZATION_URL | HTTPS authorization endpoint |
| NEXUS_AI_OAUTH_TOKEN_URL | HTTPS token endpoint |
| NEXUS_AI_OAUTH_CLIENT_ID | Public OAuth client ID for the desktop app |

All URLs must use a public DNS HTTPS host with no embedded credentials, query,
or fragment. Loopback, IP-address, and .local hosts are rejected by the
released client. Do not set these values to arbitrary services for users; a
production build should use a maintainer-controlled identity provider and
gateway hostname.

The desktop authorization flow uses:

- authorization code + PKCE (S256)
- state verification
- a temporary 127.0.0.1 loopback callback
- the OAuth scope **nexus.ai.metadata**

The client never uses a desktop client secret. Store only the gateway access
and refresh session in the current user's DPAPI-protected session file.

## Metadata endpoint

After explicit user opt-in, sign-in, and a selected-item request, the client
sends an HTTPS POST to:

~~~text
/v1/metadata/lookup
~~~

The JSON body has exactly these optional fields plus required title:

~~~json
{
  "title": "Example game",
  "provider": "Steam",
  "publisher": "Example Studio",
  "version": "1.2.3",
  "executableFileName": "ExampleGame.exe",
  "parentFolderName": "Win64"
}
~~~

The desktop app will reject a result unless it is JSON with bounded, printable
text and this shape:

~~~json
{
  "canonicalTitle": "Example Game",
  "description": "Optional concise description",
  "genres": ["Action"],
  "tags": ["Co-op"],
  "confidence": 0.92
}
~~~

The gateway must not treat the title as proof of ownership or generate an
install, download, launch, or provider URL. The desktop app presents results
for review and only adds missing descriptive metadata after approval.

## Required gateway controls

Before connecting a public release, the gateway operator must provide:

- a real user identity/consent experience and supported OAuth registration
- authorization for the metadata scope
- per-user and per-IP rate limits, quotas, monitoring, and abuse handling
- a published privacy/retention policy and an incident/revocation process
- strict request/response validation and request-size limits
- server-side storage for any model-provider credential, never a desktop key
- model-output validation, logging redaction, and no arbitrary tool execution

If the gateway calls OpenAI, follow the current official OpenAI authentication
guidance. Keep application credentials server-side; use workload identity
federation where it is available and appropriate, or a server-side secret
manager for any other supported credential. Never distribute a model-provider
credential in Nexus or ask an end user to paste one into the launcher.

## Out of scope in v0.2.0

This release does not deploy a gateway, semantic library search, automatic
classification, AI store ranking, background enrichment, or a metadata cache.
Those features require their own privacy controls, reviewable behavior,
response contracts, test coverage, and release notes.
