using AutoMapper;
using GameHubz.Common.Consts;
using GameHubz.Common.Interfaces;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : BasicGenericController<UserService, UserEntity, UserDto, UserPost, UserEdit>
    {
        private readonly IMapper mapper;
        private readonly IUserContextReader userContextReader;

        public UserController(
            UserService service,
            AppAuthorizationService appAuthorizationService,
            IMapper mapper,
            IUserContextReader userContextReader)
            : base(service, appAuthorizationService)
        {
            this.mapper = mapper;
            this.userContextReader = userContextReader;
        }

        [HttpGet("lookup")]
        public async Task<IEnumerable<LookupResponse>> GetUserLookup()
        {
            await this.AppAuthorizationService.CheckAuthorization(this.UserRolesRead());

            return await this.Service.GetUserLookup();
        }

        [HttpGet("userRolesLookup")]
        public async Task<IEnumerable<LookupResponse>> GetUserRoleLookup()
        {
            await this.AppAuthorizationService.CheckAuthorization(this.UserRolesRead());

            return await this.Service.GetUserRoleLookups();
        }

        [HttpGet("{id}/edit")]
        public async Task<UserEdit> GetUserEdit(Guid id)
        {
            await this.AppAuthorizationService.CheckAuthorization(this.UserRolesRead());

            var user = await this.Service.GetUserEdit(id);
            return this.mapper.Map<UserEdit>(user);
        }

        [HttpGet("loggedUserProfile")]
        public async Task<UserDto> GetLoggedUserProfile()
        {
            await this.AppAuthorizationService.CheckAuthorization(this.UserRolesRead());

            var tokenUserInfo = await this.userContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var user = await this.Service.GetUserByTokenUserInfo(tokenUserInfo);

            return this.mapper.Map<UserDto>(user);
        }

        protected override UserRoleEnum[]? UserRolesDelete()
        {
            return
            [
                UserRoleEnum.Admin
            ];
        }

        protected override UserRoleEnum[]? UserRolesRead()
        {
            return
            [
                UserRoleEnum.Admin
            ];
        }
    }
}