// Program.Secrets.Template.cs
//
// Copy this file to Program.Secrets.cs and replace the example values with
// those for the RDS provider. Program.cs contains no provider-specific data.
//
// An optional RDS-install.config file beside RDS-install.exe may override any
// of these settings for one deployment. RDS-install never writes that file.

public static partial class Program
{
    public static string ProviderName = "<RDS-PROVIDER-NAME>";
    public static string ServiceName = "<RDS-SERVICE-NAME>";

    // Public URL used to reach the NetBird VPN.
	// Include the protocol "https://..."
    public static string PublicServerUrl = "<PUBLIC-SERVER-URL>";

    // VPN URL used to reach RustDesk and Valet.
	// Include the protocol, e.g. "http://..."
    public static string VpnServerUrl = "<VPN-SERVER-URL>";

    // NetBird setup keys that assign the peer to the appropriate group.
    public static string NetBirdGuestKey = "<ENTER-GUEST-KEY>";
    public static string NetBirdSubscriberKey = "<ENTER-SUBSCRIBER-KEY>";

    // RustDesk server configuration.
    public static string RustDeskKey = "<ENTER-RUSTDESK-PUBLIC-KEY>";
    public static int RustDeskPort = 21116;
}
