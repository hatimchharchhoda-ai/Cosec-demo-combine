using COSEC_demo.DTOs;
using FluentValidation;

namespace COSEC_demo.Validators
{
    public class LoginRequestValidator : AbstractValidator<LoginRequestDto>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.LoginUserID).NotEmpty();
            RuleFor(x => x.LoginPassword).NotEmpty();
        }
    }
}
