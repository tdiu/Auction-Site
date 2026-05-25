using System.ComponentModel.DataAnnotations;
using API.DTOs;
using Xunit;

namespace API.Tests.DTOs;

public class RegisterDtoTests
{
    [Fact]
    public void Validate_AllValidFields_ReturnsNoErrors()
    {
        var dto = new RegisterDto
        {
            DisplayName = "ValidUser",
            Email = "valid@test.com",
            Password = "Pass123",
            DateOfBirth = new DateOnly(2000, 1, 1)
        };

        var results = Validate(dto);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_EmptyDisplayName_ReturnsError()
    {
        var dto = new RegisterDto { DisplayName = "", Email = "test@test.com", Password = "Pass123" };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterDto.DisplayName)));
    }

    [Fact]
    public void Validate_EmptyEmail_ReturnsError()
    {
        var dto = new RegisterDto { DisplayName = "User", Email = "", Password = "Pass123" };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterDto.Email)));
    }

    [Fact]
    public void Validate_EmptyPassword_ReturnsError()
    {
        var dto = new RegisterDto { DisplayName = "User", Email = "test@test.com", Password = "" };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterDto.Password)));
    }

    [Fact]
    public void Validate_PasswordBelowMinLength_ReturnsError()
    {
        var dto = new RegisterDto { DisplayName = "User", Email = "test@test.com", Password = "Abc" };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterDto.Password)));
    }

    [Fact]
    public void Validate_PasswordAtMinLength_ReturnsNoPasswordError()
    {
        var dto = new RegisterDto { DisplayName = "User", Email = "test@test.com", Password = "Abcd" };

        var results = Validate(dto);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(RegisterDto.Password)));
    }

    [Fact]
    public void Validate_PasswordAtMaxLength_ReturnsNoPasswordError()
    {
        var dto = new RegisterDto { DisplayName = "User", Email = "test@test.com", Password = new string('a', 15) };

        var results = Validate(dto);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(RegisterDto.Password)));
    }

    [Fact]
    public void Validate_PasswordAboveMaxLength_ReturnsError()
    {
        var dto = new RegisterDto { DisplayName = "User", Email = "test@test.com", Password = new string('a', 16) };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterDto.Password)));
    }

    private static List<ValidationResult> Validate(RegisterDto dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, true);
        return results;
    }
}
