namespace FastMediatR;

/// <summary>
/// Common, response-agnostic marker base for every request type sent through
/// <see cref="ISender"/>.
/// </summary>
/// <remarks>
/// <para>
/// This non-generic base exists so that the untyped <see cref="ISender.Send(object, System.Threading.CancellationToken)"/>
/// overload, the source generator, and reflection-based diagnostic tooling can identify any
/// request instance without knowing its response type at compile time. Consumers should not
/// implement <see cref="IBaseRequest"/> directly — implement
/// <see cref="IRequest{TResponse}"/> or the void <see cref="IRequest"/> instead.
/// </para>
/// </remarks>
public interface IBaseRequest
{
}
