using AutoMapper;

namespace Template.Logic.Mappings
{
    //<UserEntity, UserDto, string>
    public class CustomResolver<TSource, TDestination, TDestMember> : IValueResolver<TSource, TDestination, TDestMember>
    {
        private readonly Func<TSource, TDestination, TDestMember> resolveFunc;

        public CustomResolver(Func<TSource, TDestination, TDestMember> resolveFunc)
        {
            this.resolveFunc = resolveFunc;
        }

        public TDestMember Resolve(TSource source, TDestination destination, TDestMember member, ResolutionContext context)
        {
            return this.resolveFunc(source, destination);
        }
    }
}