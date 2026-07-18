namespace API.Services.Email;

public class EmailOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseStartTls { get; set; } = false;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "auction@localhost";
    public string FromName { get; set; } = "Auction Site";
}
