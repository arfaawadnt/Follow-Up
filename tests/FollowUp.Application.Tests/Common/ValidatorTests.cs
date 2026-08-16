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
        var result = new CreateLaboratoryValidator().Validate(new CreateLaboratoryCommand { Code = "", Name = "", Segment = "Z" });
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
    public void SetRetention_enforces_the_30_day_minimum()
    {
        new SetRetentionValidator().Validate(new SetRetentionCommand(10)).IsValid.Should().BeFalse();
        new SetRetentionValidator().Validate(new SetRetentionCommand(30)).IsValid.Should().BeTrue();
    }
}
