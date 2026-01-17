using FluentValidation;
using GameHubz.Common.Consts;
using GameHubz.DataModels.Api;
using GameHubz.Logic.Crypto;
using GameHubz.Logic.Queuing.Queues;
using Microsoft.Extensions.Configuration;
using System.Net.Mail;

namespace GameHubz.Logic.Services
{
    public class UserService : AppBaseServiceGeneric<UserEntity, UserDto, UserPost, UserEdit>
    {
        private readonly IConfiguration configuration;
        private readonly AnonymousUserContextReader anonymousUserContextReader;
        private readonly DateTimeProvider dateTimeProvider;
        private readonly IPasswordHasher passwordHasher;
        private const int VerifyEmailTokenExpireHours = 48;
        private readonly EmailQueue emailQueue;
        private readonly TournamentService tournamentService;

        public UserService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            IValidator<UserEntity> validator,
            ILocalizationService localizationService,
            IConfiguration configuration,
            SearchService searchService,
            IUserContextReader userContextReader,
            ServiceFunctions serviceFunctions,
            AnonymousUserContextReader anonymousUserContextReader,
            DateTimeProvider dateTimeProvider,
            IPasswordHasher pbkdf2Hasher,
            EmailQueue emailQueue,
            TournamentService tournamentService)
            : base(
                  factory.CreateAppUnitOfWork(),
                  userContextReader,
                  localizationService,
                  searchService,
                  validator,
                  mapper,
                  serviceFunctions)
        {
            this.configuration = configuration;
            this.anonymousUserContextReader = anonymousUserContextReader;
            this.dateTimeProvider = dateTimeProvider;
            this.passwordHasher = pbkdf2Hasher;
            this.emailQueue = emailQueue;
            this.tournamentService = tournamentService;
        }

        public async Task AddUpdateUserAnonymously(UserEntity userEntity)
        {
            this.Validator.ValidateAndThrow(userEntity);

            await this.AppUnitOfWork.UserRepository.AddUpdateEntity(
                userEntity,
                this.anonymousUserContextReader);

            await this.SaveAsync();
        }

        public Task<List<LookupResponse>> GetUserLookup()
        {
            return this.AppUnitOfWork.UserRepository.GetUserLookups();
        }

        public Task<List<LookupResponse>> GetUserRoleLookups()
        {
            return this.AppUnitOfWork.UserRoleRepository.GetUserRoleLookups();
        }

        public Task<UserEntity?> GetUserEdit(Guid id)
        {
            return this.AppUnitOfWork.UserRepository.GetByIdForEdit(id);
        }

        public Task<UserEntity?> GetUserByTokenUserInfo(TokenUserInfo tokenUserInfo)
        {
            return this.AppUnitOfWork.UserRepository.GetById(tokenUserInfo.UserId);
        }

        protected override IRepository<UserEntity> GetRepository()
        {
            return this.AppUnitOfWork.UserRepository;
        }

        public async Task RegisterUser(RegisterUserPostDto registerUserPostDto)
        {
            if (await this.AppUnitOfWork.UserRepository.AnyByEmail(registerUserPostDto.Email))
            {
                throw new UserAlreadyExistsException(this.LocalizationService);
            }

            UserEntity user = this.Mapper.Map<UserEntity>(registerUserPostDto);

            ValidateEmailField(user);

            if (string.IsNullOrEmpty(user.Password))
            {
                throw new EmptyPasswordException(this.LocalizationService);
            }

            user.PasswordNonce = NonceGenerator.GetNew();
            user.Password = this.HashPassword(user.Password, user.PasswordNonce);

            GenerateVerifyEmailToken(user);

            await this.AddUpdateUserAnonymously(user);

            await this.SendVerificationEmail(user);
        }

        public async Task ResendVerificationEmail(ResendVerificationRequestDto resendVerificationRequestDto)
        {
            UserEntity? user = await this.AppUnitOfWork.UserRepository.ShallowGetByEmail(resendVerificationRequestDto.Email);

            if (user == null)
            {
                throw new EntityNotFoundException("Resend verification email", "UserEntity", this.LocalizationService);
            }

            if (user.IsVerified)
            {
                throw new UserIsAlreadyVerifiedException(this.LocalizationService);
            }

            GenerateVerifyEmailToken(user);

            await this.AddUpdateUserAnonymously(user);

            await this.SendVerificationEmail(user);
        }

        public async Task VerifyEmail(Guid verifyEmailToken)
        {
            UserEntity? user = await this.AppUnitOfWork.UserRepository.GetByVerifyEmailToken(verifyEmailToken);

            if (user == null)
            {
                throw new EntityNotFoundException("Verify Email Token", "UserEntity", this.LocalizationService);
            }

            if (user.VerifyEmailTokenExpires < this.dateTimeProvider.Now())
            {
                throw new InvalidVerifyEmailTokenException(this.LocalizationService);
            }

            MarkUserAsVerified(user);

            await this.AddUpdateUserAnonymously(user);
        }

        public async Task SendVerificationEmail(UserEntity userEntity)
        {
            string url = $"{configuration["BaseUrl"]}/api/Auth/verifyEmail";
            string message = $"{url}?verifyEmailToken={userEntity.VerifyEmailToken}";

            EmailQueueModel emailQueue = new()
            {
                To = userEntity.Email,
                Subject = "Verify email",
                Message = message,
                IsMessageHtml = true
            };

            await this.emailQueue.Enqueue(emailQueue);
        }

        public async Task FollowHub(HubFollowRequest request)
        {
            var userId = request.UserId ?? (await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull()).UserId;

            var entity = new UserHubEntity
            {
                HubId = request.HubId,
                UserId = userId
            };

            await this.AppUnitOfWork.UserHubRepository.AddEntity(entity, UserContextReader);

            await this.SaveAsync();
        }

        public async Task<TournamentPagedResponse> GetTournamentsPaged(Guid id, UserTournamentRequest request)
        {
            return await this.tournamentService.GetTournamentPagedForUser(id, request);
        }

        protected override async Task BeforeSave(UserEntity entity, UserPost inputDto, bool isNew)
        {
            if (inputDto.Id.HasValue == false)
            {
                throw new NullReferenceException($"UserService.BeforeSave: {nameof(inputDto.Id)}");
            }

            await this.CheckPasswordField(entity, inputDto, isNew);

            ValidateEmailField(entity);

            if ((await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull()).RoleEnum == UserRoleEnum.Admin)
            {
                MarkUserAsVerified(entity);

                return;
            }

            if (isNew)
            {
                GenerateVerifyEmailToken(entity);

                await this.SendVerificationEmail(entity);

                return;
            }

            UserEntity currentUser = await this.AppUnitOfWork.UserRepository.GetByIdOrThrowIfNull(inputDto.Id.Value);

            if (currentUser.Email != entity.Email)
            {
                GenerateVerifyEmailToken(entity);

                await this.SendVerificationEmail(entity);
            }
        }

        private void MarkUserAsVerified(UserEntity userEntity)
        {
            userEntity.IsVerified = true;
            userEntity.VerifyEmailToken = null;
            userEntity.VerifyEmailTokenExpires = null;
        }

        private void GenerateVerifyEmailToken(UserEntity userEntity)
        {
            userEntity.IsVerified = false;
            userEntity.VerifyEmailToken = Guid.NewGuid();
            userEntity.VerifyEmailTokenExpires = this.dateTimeProvider.Now().AddHours(VerifyEmailTokenExpireHours);
        }

        private async Task CheckPasswordField(UserEntity entity, UserPost userPost, bool isNew)
        {
            if (isNew)
            {
                if (string.IsNullOrEmpty(userPost.Password))
                {
                    throw new EmptyPasswordException(this.LocalizationService);
                }

                entity.PasswordNonce = NonceGenerator.GetNew();
                entity.Password = this.HashPassword(userPost.Password, entity.PasswordNonce);
            }
            else
            {
                if (string.IsNullOrEmpty(userPost.Password))
                {
                    // If no new password is sent in POST, set current password into entity

                    UserEntity user
                        = await this.AppUnitOfWork.UserRepository
                            .GetByIdOrThrowIfNull(userPost.Id!.Value);

                    entity.Password = user.Password;
                }
                else
                {
                    // entity exists but password was updated
                    // use same PasswordNonce
                    entity.Password = this.HashPassword(userPost.Password, entity.PasswordNonce);
                }
            }
        }

        private string HashPassword(string password, string salt)
        {
            return this.passwordHasher.HashPassword(password, salt);
        }

        private static void ValidateEmailField(UserEntity entity)
        {
            try
            {
                var emailAddress = new MailAddress(entity.Email);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }
}