# Entity Framework Core Encrypted

[![Build and Test](https://github.com/starushykart/ef-core-encryption/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/build-and-test.yml)
[![CodeQL](https://github.com/starushykart/ef-core-encryption/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/github-code-scanning/codeql)
[![Qodana](https://github.com/starushykart/ef-core-encryption/actions/workflows/code_quality.yml/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/code_quality.yml)
[![codecov](https://codecov.io/github/starushykart/ef-core-encryption/graph/badge.svg?token=C1JOFN38GC)](https://codecov.io/github/starushykart/ef-core-encryption)

## Disclaimer
This project is an extension of [Microsoft Entity Framework Core](https://github.com/aspnet/EntityFrameworkCore) that adds support for encrypted properties using built-in or custom encryption providers.  
  
The authors **do not accept any responsibility** if you use or deploy this in a production environment and lose your encryption key or corrupt your data. Users are advised to thoroughly test and validate integration before using it in any production environment.
  
By using or contributing to this repository, you agree to follow its terms.

## NuGet
|                                                    |      |                                                                                                                                                                                         |                                                                                                                                                                                             |
|----------------------------------------------------|:----:|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|
| EntityFrameworkCore.Encrypted                      |.NET 8| [![NuGet Version](https://img.shields.io/nuget/v/EntityFrameworkCore.Encrypted)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted)                                          | [![NuGet Downloads](https://img.shields.io/nuget/dt/EntityFrameworkCore.Encrypted)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted)                                           |
| EntityFrameworkCore.Encrypted.Postgres.AwsWrapping |.NET 8| [![NuGet Version](https://img.shields.io/nuget/v/EntityFrameworkCore.Encrypted.Postgres.AwsWrapping)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.Postgres.AwsWrapping)| [![NuGet Downloads](https://img.shields.io/nuget/dt/EntityFrameworkCore.Encrypted.Postgres.AwsWrapping)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.Postgres.AwsWrapping) |

## GitHub Issues
![GitHub Issues or Pull Requests](https://img.shields.io/github/issues-raw/starushykart/ef-core-encryption?link=https%3A%2F%2Fgithub.com%2Fstarushykart%2Fef-core-encryption%2Fissues%3Fq%3Dis%253Aopen%2Bis%253Aissue%2B)

## Build from Source

 1. Install the latest [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
 2. Clone the source codee<br/>
    ```bash
    git clone https://github.com/starushykart/ef-core-encryption.git
    ```
 3. Run `dotnet build`


