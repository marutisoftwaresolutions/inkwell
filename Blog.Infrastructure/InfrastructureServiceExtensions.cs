using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Infrastructure.Data;
using Blog.Infrastructure.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Blog.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        var context = new DapperContext(connectionString);
        services.AddSingleton(context);
        services.AddSingleton<MigrationService>();

        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<IPageRepository, PageRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<IRevisionRepository, RevisionRepository>();
        services.AddScoped<ICustomThemeSettingRepository, CustomThemeSettingRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IPageViewRepository, PageViewRepository>();
        services.AddScoped<IRedirectRepository, RedirectRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<Blog.Core.Services.AuthService>();
        services.AddScoped<ApplicationDbSeeder>();
        services.AddScoped<OptometryTaxonomySeeder>();

        // Register Dapper Type Handlers
        Dapper.SqlMapper.AddTypeHandler(new SqlGuidHandler());
        Dapper.SqlMapper.AddTypeHandler(new PostStatusTypeHandler());

        return services;
    }
}

public class SqlGuidHandler : Dapper.SqlMapper.TypeHandler<Guid>
{
    public override void SetValue(System.Data.IDbDataParameter parameter, Guid value)
    {
        parameter.Value = value;
    }

    public override Guid Parse(object value)
    {
        if (value is Guid guid) return guid;
        if (value is int intValue)
        {
            // Map integer ID to a predictable Guid: {00000000-0000-0000-0000-XXXXXXXXXXXX}
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(intValue).CopyTo(bytes, 0);
            return new Guid(bytes);
        }
        if (value is string str && Guid.TryParse(str, out var parsedGuid)) return parsedGuid;

        return Guid.Empty;
    }
}

public class PostStatusTypeHandler : Dapper.SqlMapper.TypeHandler<PostStatus>
{
    public override void SetValue(System.Data.IDbDataParameter parameter, PostStatus value)
    {
        parameter.Value = value.ToString();
    }

    public override PostStatus Parse(object value)
    {
        if (value is string str && Enum.TryParse<PostStatus>(str, ignoreCase: true, out var result))
            return result;
        if (value is int intVal && Enum.IsDefined(typeof(PostStatus), intVal))
            return (PostStatus)intVal;
        return PostStatus.Draft;
    }
}
