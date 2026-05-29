namespace FastMediatR;

/// <summary>
/// Combined mediator interface composing <see cref="ISender"/> and <see cref="IPublisher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Prefer injecting <see cref="ISender"/> or <see cref="IPublisher"/> directly to keep
/// controller and service dependencies narrow. Inject <see cref="IMediator"/> only when
/// a single class genuinely needs both sending and publishing.
/// </para>
/// </remarks>
public interface IMediator : ISender, IPublisher
{
}
