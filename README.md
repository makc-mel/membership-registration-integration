# Membership Registration Integration

HTTP-triggered Azure Function that validates a CRM registration and maps it to the legacy membership API contract.

Built with .NET 8 and the Azure Functions isolated worker model.

## Build and test

```sh
dotnet build src/Membership.Functions/Membership.Functions.csproj
dotnet test tests/Membership.Functions.Tests/Membership.Functions.Tests.csproj
```

To use Visual Studio, open the solution containing both projects and run the tests through Test Explorer.

## Run locally

Requires Azure Functions Core Tools v4 and Azurite, available through Visual Studio's Azure development tooling or installed separately.

Copy src/Membership.Functions/local.settings.json.example to local.settings.json in the same directory. Ensure Values.FUNCTIONS_WORKER_RUNTIME is set to dotnet-isolated.

Visual Studio: 
     Open src/Membership.Functions/Membership.Functions.slnx. 
     Set Membership.Functions as the startup project and press F5.
Command line: 
     Start Azurite, then run func start from src/Membership.Functions.

From the repository root:

```sh
curl -i http://localhost:7071/api/registrations/map \
  -H "Content-Type: application/json" \
  --data-binary @samples/registration.json
```

Use `curl.exe` in Windows PowerShell. For PowerShell, enter the command on one line or replace the shell line-continuation characters with PowerShell backticks.

## Endpoint

`POST /api/registrations/map`

Returns:

- **200** — the legacy JSON payload, matching `samples/expected.json`.
- **400** — a `validation_failed` response with field-level errors.

The function uses `AuthorizationLevel.Function`. When hosted in Azure, callers must supply a function key unless authentication is handled through another configured mechanism. Local execution normally does not enforce the key.

## Implementation

`RegistrationMapper` handles parsing, validation and transformation independently of the Azure Functions runtime. It returns a `MappingResult` containing either a payload or validation errors.

`MapRegistrationFunction` reads the HTTP body, invokes the mapper and translates the result into an HTTP response.

The legacy DTOs use explicit JSON property names to keep serialization aligned with the downstream contract.

## Contract decisions

- `firstName`, `lastName`, `dateOfBirth`, `email` and `membershipType` are required, nonempty strings.
- String values are trimmed before validation and mapping.
- Property names and membership types are case-sensitive.
- Membership types map as follows: `Single → S`, `Family → F`, `Couple → C`.
- Dates must be valid calendar dates in `yyyy-MM-dd` format. Other formats are rejected rather than interpreted ambiguously. Output uses `dd/MM/yyyy`, independent of the current culture.
- Email validation checks plausibility only; it does not verify deliverability.
- Unknown fields are ignored. `registeredAt` is neither required nor forwarded.
- `source` is exactly `MILKYWAY`, following the sample output contract.
- Field validation errors are collected before constructing the payload.
- No age restrictions or future-date rules are applied because they are outside the supplied requirements.

## Tests

Automated tests cover:

- The complete legacy output contract.
- Missing, null, empty and incorrectly typed required fields.
- Invalid email and membership values.
- All three plan-code mappings.
- Date conversion, invalid calendar dates and unexpected date formats.
- Extra fields and optional `registeredAt`.
- Aggregated validation errors.
- HTTP handler success and validation responses.

The handler tests invoke the function directly; they do not start the Azure Functions host.

The solution was built and all automated tests passed locally. The sample registration was also sent through the running Functions host and returned the expected legacy payload.

## Production outbound integration

The exercise stops at returning the mapped payload. The following considerations would apply when extending it to call the real legacy API; they are not implemented in this submission.

For outbound delivery, inject a typed client through `IHttpClientFactory`. Configure the base URL and the authentication required by the legacy API—for example, OAuth client credentials or an API key—externally, keeping credentials in Key Vault or an equivalent secret store.

Apply a bounded request timeout and propagate cancellation. Retry transient failures with bounded exponential backoff and jitter, respecting `Retry-After`. Do not retry ordinary validation or authentication failures.

A registration POST must not be retried blindly: the downstream system may have accepted it before a timeout. Agree an idempotency mechanism with the legacy API and add a stable registration identifier to the CRM contract.

If durable delivery is required, queue the registration and handle retries and dead-letter processing separately from the inbound request.

Log correlation IDs and sanitized failure details. Avoid logging registration bodies, personal information or credentials.
