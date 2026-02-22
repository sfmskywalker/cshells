using System.Reflection;
using CShells.Hosting;

namespace CShells.Notifications;

/// <summary>
/// Helpers for ordering shell lifecycle handlers by <see cref="ShellHandlerOrderAttribute"/>.
/// </summary>
internal static class ShellHandlerOrdering
{
    private static int GetOrder(object handler) =>
        handler.GetType().GetCustomAttribute<ShellHandlerOrderAttribute>()?.Order ?? 0;

    /// <summary>
    /// Returns <paramref name="handlers"/> sorted ascending by <see cref="ShellHandlerOrderAttribute.Order"/>
    /// (lower values first). Handlers with no attribute are treated as order <c>0</c>.
    /// Stable sort: handlers with the same order retain their registration order.
    /// </summary>
    public static IEnumerable<T> OrderForActivation<T>(this IEnumerable<T> handlers) where T : class =>
        handlers.OrderBy(GetOrder);

    /// <summary>
    /// Returns <paramref name="handlers"/> sorted descending by <see cref="ShellHandlerOrderAttribute.Order"/>
    /// (higher values first) for deactivation — the natural LIFO inverse of activation order.
    /// </summary>
    public static IEnumerable<T> OrderForDeactivation<T>(this IEnumerable<T> handlers) where T : class =>
        handlers.OrderByDescending(GetOrder);
}

