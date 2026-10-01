---
title: Health check and OpenTelemetry
nav_order: 7
---

# Health check and OpenTelemetry
{: .no_toc }

1. TOC
{:toc}

---

## Health check

```csharp
builder.Services.AddHealthChecks().AddEncryptionKeys();

app.MapHealthChecks("/health");
```

The check is healthy once the keys of every encrypted context are loaded. If some aren't loaded yet, the check loads them, which also proves that the key store is migrated and your key management service is reachable. After the first success it doesn't call anything, so it's cheap to run often.

You can set the name, failure status, tags and timeout like for any other health check:

```csharp
builder.Services.AddHealthChecks()
    .AddEncryptionKeys(name: "keys", failureStatus: HealthStatus.Degraded, tags: ["ready"]);
```

The result data lists each context with the key that's loaded, or the error that prevented loading it.

## OpenTelemetry

The library publishes traces through an `ActivitySource` and metrics through a `Meter`. Both are named `EncryptionInstrumentation.Name` (`EntityFrameworkCore.Encrypted`), so subscribing takes two lines:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(x => x.AddSource(EncryptionInstrumentation.Name))
    .WithMetrics(x => x.AddMeter(EncryptionInstrumentation.Name));
```

### Metrics

| Metric | Type | Tags | What it counts |
|--------|------|------|----------------|
| `efcore.encryption.values` | counter | `db.context`, `operation` (`encrypt`, `decrypt`) | values encrypted or decrypted |
| `efcore.encryption.decryption.failures` | counter | `db.context`, `error.type` (`tampered`, `key_not_found`, `invalid_format`) | values that couldn't be decrypted |
| `efcore.encryption.key_wrapper.duration` | histogram, seconds | `db.context`, `operation` (`generate`, `unwrap`, `rewrap`), `error.type` | how long calls to your key management service take |
| `efcore.encryption.root_key.loads` | counter | `db.context`, `trigger` (`active`, `on_demand`, `refresh`), `result` | root keys loaded into memory |
| `efcore.encryption.reencryption.values` | counter | `db.context`, `result` | values processed by re-encryption |

These are worth setting alerts on:

- **`decryption.failures` with `error.type = tampered`.** Someone changed values in the database, or copied them between columns.
- **`decryption.failures` with `error.type = key_not_found`.** A root key is missing from the key store or from your configuration.
- **`root_key.loads` with `result = failure`.** The key store or your key management service is down.

### Traces

Traces cover key management and maintenance work. Individual values aren't traced; that would be far too noisy.

| Activity | Recorded when |
|----------|---------------|
| `efcore.encryption.root_key.load` | a root key is loaded (tagged with the `trigger`) |
| `efcore.encryption.key_wrapper.generate`, `.unwrap`, `.rewrap` | your key management service is called |
| `efcore.encryption.root_key.rotate` | you call `RotateRootKeyAsync` |
| `efcore.encryption.root_key.rewrap` | you call `RewrapRootKeysAsync` |
| `efcore.encryption.key_usage` | you call `GetKeyUsageAsync` |
| `efcore.encryption.reencrypt` | you call `ReEncryptAsync` |

Every activity carries a `db.context` tag with the name of the context type.

### Capturing the startup key load

Keys are loaded as the host starts. OpenTelemetry only starts listening once its own providers are created, which usually happens a little later, so the startup load is missed. If you want to see it, create the providers right after `Build()`:

```csharp
var app = builder.Build();

app.Services.GetRequiredService<TracerProvider>();
app.Services.GetRequiredService<MeterProvider>();
```

{: .tip }
In ASP.NET Core, add `AddAspNetCoreInstrumentation()` as well. Without it, activities that start inside a request have no sampled parent, and parent-based samplers may drop them.

## Logging

Key loads, rotations, rewrapping and re-encryption progress are logged through the regular `ILogger`, under categories starting with `EntityFrameworkCore.Encrypted`. Values and keys are never logged.
