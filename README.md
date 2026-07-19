# RDS-install

This tool installs and configures [NetBird](https://netbird.io) and [RustDesk](https://rustdesk.com) on a Windows computer so that the computer can join a Remote Desktop Support (RDS) service. The installer can be customized for any RDS provider by supplying the appropriate configuration values before building the program.

The RDS platform is a secure, private, open-source, self-hosted remote-support system built around NetBird and RustDesk. The NetBird component provides an overlay Virtual Private Network, which encrypts all internal traffic. RustDesk runs within the VPN, allowing users to securely access and control other computers on the network according to defined roles: Support Clients, Helpers, and Guests.

"Self-hosted" means that anyone with an internet connection can deploy an RDS server and operate a private Remote Support service under their own control.

For more information:
- [Remote Desktop Support Service](https://wiki.mypjs.us/index.php?title=Remote_Desktop_Support_Service) - Using RDS-install on a client computer
- [Remote Desktop Support Platform](https://wiki.mypjs.us/index.php?title=Self-hosted_Remote_Desktop_Support_Platform) - Building and administering an RDS server to host your own private support service

## ✨ Features

- 👨‍💻 Installs, configures, and registers a Windows computer with an RDS service in a single operation
- 🖥 Supports Subscriber and Guest roles

## Installation and Service Roles

- Subscribers are the primary Support Clients. These are computers intended to be remotely accessed and controlled by RDS service Helpers and Guests.
- Helpers are RDS staff authorized to service (work on) any Support Client.
- Guests are RDS members with limited access to specific Support Clients for which they know the RustDesk ID and password. These are typically computers belonging to users who also administer one or more Support Clients.


## 🔧 Building the Installer

### 1. Requirements

RDS-install is a .NET console application. Building it requires one of the following:
- [Microsoft .NET SDK 9.0 or later](https://dotnet.microsoft.com)
or
- [Microsoft Visual Studio](https://visualstudio.microsoft.com) with the C#/.NET desktop development workload installed, supporting .NET 9.0 or later

### 2. Provide Secrets

Before building, you must supply provider-specific configuration values. Copy the provided template:

```
Program.Secrets.Template.cs
```

to create a file named:

```
Program.Secrets.cs
```

Fill in the required values:
- `ProviderName`  - The name of the service provider (e.g., My PJs)
- `ServiceName` - Remote Desktop Support (e.g)
- `PublicServerUrl` - Public URL used to reach the RDS NetBird VPN. (e.g., https://support.example.com)
- `VpnServerUrl` - VPN URL used to reach RustDesk and Valet (e.g., http://support.example.vpn)
- `NetBirdGuestKey` - <guest-setup-key>
- `NetBirdSubscriberKey` - <subscriber-setup-key>
- `RustDeskKey` - <rustdesk-public-key>
- `RustDeskPort` - 21116

The NetBird setup keys and RustDesk public key are obtained during RDS server setup.

**🚫 Do not commit `Program.Secrets.cs` — it is excluded by `.gitignore`.**

### 3. Build

```bash
dotnet publish
```

The resulting `RDS-install.exe` is placed in the project's Publish folder.

---

## 🖥 Usage

To install RDS as a Subscriber, open an elevated Command Prompt (Run as Administrator) and issue this command:

```bash
RDS-install.exe
```

To install as a Guest, use:

```bash
RDS-install.exe --guest
```

To see all options:

```bash
RDS-install.exe --help
```

---

## 🔐 Security Considerations

By default, the installer embeds live secrets into the binary in order to simplify deployment. These secrets allow the client to join the provider's RDS server, but they do not expose administrative privileges or backend access.

- For **maximum security**, all required data may be instead supplied by a local configuration file. To enable this alternative, the provider must provide the config file to the client via other means.

In practice, My PJs accepts the minimal risk of embedding the keys in the installer and monitors usage accordingly. While the consequences of exposure are limited, providers need to understand the implications of distributing a preconfigured installer and should rotate keys if they suspect misuse.

---

## 📜 License

This project is licensed under the **GNU GPL v3**. See the [LICENSE](LICENSE) file for details.

---

## 🤝 Contributions

RDS-install is part of the [My PJs, LLC](https://mypjs.us/) [Self-hosted Remote Desktop Support Platform](https://mypjs.us/selected-projects/) project. Project documentation is available on the [My PJs wiki](https://wiki.mypjs.us/index.php?title=Self-hosted_Remote_Desktop_Support_Platform). This repository is maintained by [Jim Wilson](https://github.com/RoyHinkley).
