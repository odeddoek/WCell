using System.Collections.Generic;
using NHibernate.Engine;
using NHibernate.Persister.Entity;
using NHibernate.SqlCommand;
using NHibernate.Type;
using NHibernate.Util;

namespace NHibernate.Loader
{
	public abstract class AbstractEntityJoinWalker : JoinWalker
	{
		private readonly IOuterJoinLoadable persister;
		private readonly string alias;

		public AbstractEntityJoinWalker(IOuterJoinLoadable persister, ISessionFactoryImplementor factory,
																		IDictionary<string, IFilter> enabledFilters)
			: base(factory, enabledFilters)
		{
			this.persister = persister;
			alias = GenerateRootAlias(persister.EntityName);
		}

		public AbstractEntityJoinWalker(string rootSqlAlias, IOuterJoinLoadable persister, ISessionFactoryImplementor factory,
																		IDictionary<string, IFilter> enabledFilters)
			: base(factory, enabledFilters)
		{
			this.persister = persister;
			alias = rootSqlAlias;
		}

		protected void InitAll(SqlString whereString,string orderByString,LockMode lockMode)
		{
			WalkEntityTree(persister, Alias);
			IList<OuterJoinableAssociation> allAssociations = new List<OuterJoinableAssociation>(associations);
			allAssociations.Add(
				new OuterJoinableAssociation(persister.EntityType, null, null, alias, JoinType.LeftOuterJoin, Factory,
				                             new CollectionHelper.EmptyMapClass<string, IFilter>()));

			InitPersisters(allAssociations, lockMode);
			InitStatementString(whereString, orderByString, lockMode);
		}

		protected void InitProjection(SqlString projectionString, SqlString whereString,
			string orderByString, string groupByString, LockMode lockMode)
		{
			WalkEntityTree(persister, Alias);
			Persisters = new ILoadable[0];
			InitStatementString(projectionString, whereString, orderByString, groupByString, lockMode);
		}

		private void InitStatementString(SqlString condition, string orderBy, LockMode lockMode)
		{
			InitStatementString(null, condition, orderBy, string.Empty, lockMode);
		}

		private void InitStatementString(SqlString projection,SqlString condition,
			string orderBy,string groupBy,LockMode lockMode)
		{
			int joins = CountEntityPersisters(associations);
			Suffixes = BasicLoader.GenerateSuffixes(joins + 1);
			JoinFragment ojf = MergeOuterJoins(associations);

			SqlString selectClause = projection
			                         ??
			                         new SqlString(persister.SelectFragment(alias, Suffixes[joins]) + SelectString(associations));
			
			SqlSelectBuilder select = new SqlSelectBuilder(Factory)
				.SetLockMode(lockMode)
				.SetSelectClause(selectClause)
				.SetFromClause(Dialect.AppendLockHint(lockMode, persister.FromTableFragment(alias)) +persister.FromJoinFragment(alias, true, true))
				.SetWhereClause(condition)
				.SetOuterJoins(ojf.ToFromFragmentString,ojf.ToWhereFragmentString + WhereFragment)
				.SetOrderByClause(OrderBy(associations, orderBy))
				.SetGroupByClause(groupBy);

			if (Factory.Settings.IsCommentsEnabled)
				select.SetComment(Comment);

			SqlString = select.ToSqlString();
		}

		/// <summary>
		/// The superclass deliberately excludes collections
		/// </summary>
		protected override bool IsJoinedFetchEnabled(IAssociationType type, FetchMode config,
		                                             CascadeStyle cascadeStyle)
		{
			return IsJoinedFetchEnabledInMapping(config, type);
		}

		/// <summary>
		/// Don't bother with the discriminator, unless overridden by subclass
		/// </summary>
		protected virtual SqlString WhereFragment
		{
			get { return persister.WhereJoinFragment(alias, true, true); }
		}

		public abstract string Comment { get; }

		protected ILoadable Persister
		{
			get { return persister; }
		}

		protected string Alias
		{
			get { return alias; }
		}

		public override string ToString()
		{
			return GetType().FullName + '(' + Persister.EntityName + ')';
		}
	}
}