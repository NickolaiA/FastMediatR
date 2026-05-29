using FastMediatR;
using FluentAssertions;
using Xunit;

namespace FastMediatR.Tests.Core;

/// <summary>
/// Compile-time + reflection smoke tests for the public abstractions in <c>FastMediatR.Core</c>.
/// Covers task 1.T: every public interface and the <c>IRequest : IRequest&lt;Unit&gt;</c> inheritance contract.
/// </summary>
public sealed class CoreAbstractionsTests
{
    /// <summary>
    /// <see cref="IRequest"/> must inherit from <see cref="IRequest{Unit}"/> so void commands flow
    /// through the same dispatch path as response-returning commands. This is the central
    /// "no special-casing for void" guarantee that lets the source generator emit one switch.
    /// </summary>
    [Fact]
    public void IRequest_Inherits_From_IRequest_Of_Unit()
    {
        typeof(IRequest<Unit>).IsAssignableFrom(typeof(IRequest)).Should().BeTrue();
    }

    /// <summary><see cref="IRequest{TResponse}"/> must derive from the non-generic marker.</summary>
    [Fact]
    public void IRequest_Of_T_Inherits_From_IBaseRequest()
    {
        typeof(IBaseRequest).IsAssignableFrom(typeof(IRequest<string>)).Should().BeTrue();
    }

    /// <summary><see cref="IMediator"/> must implement both sender and publisher.</summary>
    [Fact]
    public void IMediator_Implements_ISender_And_IPublisher()
    {
        typeof(ISender).IsAssignableFrom(typeof(IMediator)).Should().BeTrue();
        typeof(IPublisher).IsAssignableFrom(typeof(IMediator)).Should().BeTrue();
    }

    /// <summary>The <c>FastMediatR</c> public surface lives in the <c>FastMediatR</c> namespace only.</summary>
    [Theory]
    [InlineData(typeof(Unit))]
    [InlineData(typeof(IBaseRequest))]
    [InlineData(typeof(IRequest))]
    [InlineData(typeof(INotification))]
    [InlineData(typeof(ISender))]
    [InlineData(typeof(IPublisher))]
    [InlineData(typeof(IMediator))]
    [InlineData(typeof(INotificationPublisher))]
    [InlineData(typeof(NotificationHandlerExecutor))]
    [InlineData(typeof(HandlerLifetimeAttribute))]
    public void Public_Types_Live_In_FastMediatR_Namespace(Type type)
    {
        type.Namespace.Should().Be("FastMediatR");
    }

    /// <summary>Concrete commands typed with a response must be assignable to <see cref="IBaseRequest"/>.</summary>
    [Fact]
    public void Concrete_Request_Is_IBaseRequest()
    {
        IBaseRequest r = new SampleRequest();
        r.Should().BeAssignableTo<IRequest<int>>();
    }

    /// <summary>A void command (<see cref="IRequest"/>) must also be an <see cref="IRequest{Unit}"/>.</summary>
    [Fact]
    public void Void_Command_Is_IRequest_Of_Unit()
    {
        IRequest<Unit> r = new SampleVoidCommand();
        r.Should().NotBeNull();
    }

    /// <summary>A notification must be assignable to <see cref="INotification"/>.</summary>
    [Fact]
    public void Concrete_Notification_Implements_INotification()
    {
        INotification n = new SampleNotification();
        n.Should().NotBeNull();
    }

    private sealed record SampleRequest : IRequest<int>;

    private sealed record SampleVoidCommand : IRequest;

    private sealed record SampleNotification : INotification;
}
