# RDS-install

This tool installs and configures [NetBird](https://netbird.io) and [RustDesk](https://rustdesk.com) to enable My PJs **secure, remote desktop access** to subscribers. It is intended for those who need remote support or remote access capabilities.

## ✨ Features

- 👨‍💻 Installs and configures NetBird VPN
- 🖥 Installs and preconfigures RustDesk remote desktop
- 🔐 Communicates securely with My PJs' internal services
- 🧹 Supports two roles:
  - **Subscribers** (for systems serviced by My PJs)
  - **Guests** (for users who want remote access to their own hosts)

## 🔧 Building the Installer

This is a .NET console app. Before building, you must supply your own configuration keys.

### 1. Provide Secrets

Before building, use the provided template:

```
Program.Secrets.Template.cs
```

to create a file named:

```
Program.Secrets.cs
```

Fill in the required values:

- `NetBirdGuestKey` – setup key for Guests (Guests can remote into any RDS subscriber whose RustDesk ID and password they know.)
- `NetBirdSubscriberKey` – setup key for RDS subscribers (My PJs support staff ("Helpers") can remote into RDS subscribers.)
- `MgmtUrl` – NetBird service URL (support.mypjs.us)
- `RustDeskKey` – RustDesk server public key
- `ValetUrl` – My PJs internal service (support.mypjs.vpn)

**🚫 Do not commit `Program.Secrets.cs` — it is excluded by `.gitignore`.**

### 2. Build a self-contained release

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The resulting `RDS-install.exe` is a single-file executable suitable for distribution.

---

## 🖥 Usage

To install the My PJs RDS:

```bash
RDS-install.exe
```

To install as a **guest** (for users who need to remote into another RDS computer):

```bash
RDS-install.exe --guest
```

To see all options:

```bash
RDS-install.exe --help
```

---

## 🔐 Security Considerations

This installer embeds live secrets (e.g., NetBird setup keys, RustDesk server key) in order to simplify deployment. These secrets allow systems to register with My PJs' RDS but do not expose administrative privileges or backend access.

- For **maximum security**, all secrets can be supplied via command-line arguments at runtime.
- In practice, we accept the minimal risk of embedding keys in the installer and monitor usage accordingly.
- The `ValetUrl` is only accessible over VPN, and other embedded keys are scoped to their purpose.
- If needed, you can rotate any exposed key using the NetBird or RustDesk management interfaces.

While the consequences of exposure are limited, users are encouraged to understand the implications of distributing preconfigured installers and to rotate keys if they suspect misuse.

---

## 📜 License

This project is licensed under the **GNU GPL v3**. See the [LICENSE](LICENSE) file for details.

---

## 🤝 Contributions

This repository is maintained by [Jim Wilson of My PJs, LLC](https://github.com/RoyHinkley). Issues, feedback, and forks are welcome — please review your changes for security implications before publishing.

