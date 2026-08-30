using FluentAssertions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Setup;
using FollowUp.Application.Features.UserAdmin.Users;

namespace FollowUp.Application.Tests.Common;

public class ValidatorTests
{
    [Fact]
    public void CreateLaboratory_rejects_empty_fields_and_bad_segment()
    {
        var result = new CreateLaboratoryValidator().Validate(new CreateLaboratoryCommand { Code = "", Name = "", Segment = "" });
        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().Contain(new[] { "Code", "Name", "Segment" });
    }

    [Fact]
    public void CreateLaboratory_accepts_a_valid_command()
    {
        new CreateLaboratoryValidator().Validate(new CreateLaboratoryCommand { Code = "MGL-1", Name = "Lab", Segment = "A" })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateUser_requires_a_minimum_password_length()
    {
        var result = new CreateUserValidator().Validate(new CreateUserCommand { Username = "u", Password = "short", RoleId = System.Guid.NewGuid() });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void CreateUser_enforces_password_complexity_and_a_deny_list()
    {
        // IDN-10: length alone is not enough — require mixed case + a digit, and reject common choices.
        static bool Valid(string pw) => new CreateUserValidator()
            .Validate(new CreateUserCommand { Username = "u", Password = pw, RoleId = System.Guid.NewGuid() }).IsValid;

        Valid("alllower12345").Should().BeFalse();       // no upper-case
        Valid("ALLUPPER12345").Should().BeFalse();       // no lower-case
        Valid("NoDigitsHere!").Should().BeFalse();       // no digit
        Valid("Password1").Should().BeFalse();           // passes complexity but is on the deny-list
        Valid("Str0ng-Passphrase").Should().BeTrue();    // length + complexity + not common
    }

    [Fact]
    public void SetRetention_enforces_the_30_day_minimum()
    {
        new SetRetentionValidator().Validate(new SetRetentionCommand(10)).IsValid.Should().BeFalse();
        new SetRetentionValidator().Validate(new SetRetentionCommand(30)).IsValid.Should().BeTrue();
    }
}
