using AutoMapper;

namespace Template.Logic.Mappings
{
    public class UserProfile : Profile
    {
        public UserProfile()
        {
            this.CreateMap<RegisterUserPostDto, UserEntity>();

            this.CreateMap<UserPost, UserEntity>();

            this.CreateMap<UserEntity, UserDto>()
                .ForMember(x => x.UserRoleDisplayName, m => m.MapFrom(x => x.UserRole.DisplayName))
                .ForMember(x => x.UserRoleSystemName, m => m.MapFrom(x => x.UserRole.SystemName));

            this.CreateMap<UserEntity, TokenUserInfo>()
                .ForMember(x => x.Role, m => m.MapFrom(x => x.UserRole.SystemName))
                .ForMember(x => x.UserId, m => m.MapFrom(x => x.Id));

            this.CreateMap<UserRoleEntity, LookupResponse>();
        }
    }
}