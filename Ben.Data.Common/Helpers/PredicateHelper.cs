using System.Linq.Expressions;
using System.Reflection;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// Utility methods for composing LINQ <see cref="Expression{TDelegate}"/> predicates at runtime.
/// </summary>
/// <remarks>
/// Useful when building dynamic query filters where clauses are added
/// conditionally.  The <see cref="Or{T}"/> and <see cref="And{T}"/> combinators
/// re-use a single parameter expression across both branches so that EF Core
/// can translate the combined expression to SQL without ambiguous parameter names.
/// <para>
/// The internal <c>Replace</c> helper uses reflection to walk the expression tree
/// and substitute one parameter reference with another — this is necessary because
/// two independently created lambda expressions have distinct parameter objects even
/// when they have the same name.
/// </para>
/// </remarks>
public static class PredicateHelper
{
    /// <summary>
    /// Returns a typed <c>null</c> predicate suitable as a starting point for
    /// building up a filter with <see cref="Or{T}"/> or <see cref="And{T}"/>.
    /// </summary>
    /// <typeparam name="T">The entity type the predicate is for.</typeparam>
    /// <returns><c>null</c> — treated as "no filter" by the <see cref="Or{T}"/> and <see cref="And{T}"/> overloads.</returns>
    public static Expression<Func<T, bool>>? Get<T>() { return null; }

    /// <summary>
    /// Identity overload — returns the supplied predicate unchanged.
    /// </summary>
    /// <typeparam name="T">The entity type the predicate is for.</typeparam>
    /// <param name="predicate">An existing predicate expression.</param>
    /// <returns>The same <paramref name="predicate"/> instance.</returns>
    public static Expression<Func<T, bool>> Get<T>(this Expression<Func<T, bool>> predicate)
    {
        return predicate;
    }

    /// <summary>
    /// Combines two predicates with a logical OR (<c>|</c>).
    /// </summary>
    /// <typeparam name="T">The entity type the predicate is for.</typeparam>
    /// <param name="expr">The left-hand predicate.  If <c>null</c>, <paramref name="or"/> is returned directly.</param>
    /// <param name="or">The right-hand predicate to combine.</param>
    /// <returns>A new expression equivalent to <c>expr | or</c> sharing a single parameter.</returns>
    /// <remarks>
    /// When <paramref name="expr"/> is <c>null</c> this returns <paramref name="or"/> unchanged,
    /// allowing a fluid start from <see cref="Get{T}()"/>.
    /// </remarks>
    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> expr, Expression<Func<T, bool>> or)
    {
        if (expr == null) return or;
        Replace(or, or.Parameters[0], expr.Parameters[0]);
        return Expression.Lambda<Func<T, bool>>(Expression.Or(expr.Body, or.Body), expr.Parameters);
    }

    /// <summary>
    /// Combines two predicates with a logical AND (<c>&amp;</c>).
    /// </summary>
    /// <typeparam name="T">The entity type the predicate is for.</typeparam>
    /// <param name="expr">The left-hand predicate.  If <c>null</c>, <paramref name="and"/> is returned directly.</param>
    /// <param name="and">The right-hand predicate to combine.</param>
    /// <returns>A new expression equivalent to <c>expr &amp; and</c> sharing a single parameter.</returns>
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> expr, Expression<Func<T, bool>> and)
    {
        if (expr == null) return and;
        Replace(and, and.Parameters[0], expr.Parameters[0]);
        return Expression.Lambda<Func<T, bool>>(Expression.And(expr.Body, and.Body), expr.Parameters);
    }

    /// <summary>
    /// Recursively walks the object graph of an expression tree node and replaces all
    /// field references to <paramref name="old"/> with <paramref name="replacement"/>.
    /// </summary>
    /// <param name="instance">The expression node to walk.</param>
    /// <param name="old">The parameter expression to replace.</param>
    /// <param name="replacement">The substitute parameter expression.</param>
    /// <remarks>
    /// This is necessary because two independently created lambda expressions have
    /// distinct <see cref="System.Linq.Expressions.ParameterExpression"/> objects even
    /// when they share the same name.  Only fields whose declaring type lives in the
    /// <c>System.Linq.Expressions</c> assembly are walked to avoid replacing unrelated objects.
    /// </remarks>
    static void Replace(object instance, object old, object replacement)
    {
        for (Type? t = instance.GetType(); t != null; t = t.BaseType)
            foreach (FieldInfo fi in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object? val = fi.GetValue(instance);
                if (val != null && val.GetType().Assembly == typeof(Expression).Assembly)
                    if (object.ReferenceEquals(val, old))
                        fi.SetValue(instance, replacement);
                    else
                        Replace(val, old, replacement);
            }
    }
}

