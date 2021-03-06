﻿using Dapper;
using LnskyDB.Expressions;
using LnskyDB.Helper;
using LnskyDB.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace LnskyDB.Internal
{
    internal class JoinQueryInfo<T> : IJoinQuery<T>
    {
        public long StarSize { get; set; }
        public int Rows { get; set; }

        private int JoinTableCount { get; set; }
        public DynamicParameters Param { get; set; }
        public BaseDBModel DBModel { get; set; }
        public string JoinStr { get; set; }
        public string Where { get; set; }
        public Dictionary<string, string> Map { get; set; }

        public List<OrderCriteria> OrderbyList { get; private set; } = new List<OrderCriteria>();
        public LambdaExpression WhereExpression { get; private set; }

        public JoinQueryInfo(BaseDBModel dBModel, string joinStr, int joinTableCount, Dictionary<string, string> map, string where, DynamicParameters parameters)
        {
            JoinStr = joinStr;
            JoinTableCount = joinTableCount;
            Map = map;
            Where = where;
            Param = parameters;
            DBModel = dBModel;
        }
        public IJoinQuery<T> And(Expression<Func<T, bool>> predicate)
        {
            WhereExpression = WhereExpression == null ? predicate : ((Expression<Func<T, bool>>)WhereExpression).And(predicate);
            return this;
        }
        public IJoinQuery<T> Or(Expression<Func<T, bool>> predicate)
        {
            WhereExpression = WhereExpression == null ? predicate : ((Expression<Func<T, bool>>)WhereExpression).Or(predicate);
            return this;
        }
        public IJoinQuery<T> OrderBy<TProperty>(Expression<Func<T, TProperty>> field)
        {
            if (field != null)
                OrderbyList.Add(new OrderCriteria { OrderBy = EOrderBy.Asc, Field = field });
            return this;
        }

        public IJoinQuery<T> OrderByDescing<TProperty>(Expression<Func<T, TProperty>> field)
        {
            if (field != null)
                OrderbyList.Add(new OrderCriteria { OrderBy = EOrderBy.Desc, Field = field });
            return this;
        }
        public IJoinQuery<TResult> OuterJoin<TR, TKey, TResult>(IQuery<TR> rightQuery, Expression<Func<T, TKey>> leftKeySelector, Expression<Func<TR, TKey>> rightKeySelector, Expression<Func<T, TR, TResult>> resultSelector) where TR : BaseDBModel, new()
        {
            return Join("OUTER", rightQuery, leftKeySelector, rightKeySelector, resultSelector);
        }
        public IJoinQuery<TResult> InnerJoin<TR, TKey, TResult>(IQuery<TR> rightQuery, Expression<Func<T, TKey>> leftKeySelector, Expression<Func<TR, TKey>> rightKeySelector, Expression<Func<T, TR, TResult>> resultSelector) where TR : BaseDBModel, new()
        {
            return Join("INNER", rightQuery, leftKeySelector, rightKeySelector, resultSelector);
        }

        private IJoinQuery<TResult> Join<TR, TKey, TResult>(string type, IQuery<TR> rightQuery, Expression<Func<T, TKey>> leftKeySelector, Expression<Func<TR, TKey>> rightKeySelector, Expression<Func<T, TR, TResult>> resultSelector) where TR : BaseDBModel, new()
        {
            var rightTableAlias = "t" + (JoinTableCount + 1);
            var dynamicParameters = new DynamicParameters();
            dynamicParameters.AddDynamicParams(Param);
            var left = new JoinExpression(leftKeySelector, Map, dynamicParameters, DBModel.GetDBModel_SqlProvider());

            var right = new JoinExpression(rightKeySelector, new Dictionary<string, string> { { "", rightTableAlias } }, dynamicParameters, DBModel.GetDBModel_SqlProvider());
            StringBuilder sqlJoin = new StringBuilder();
            foreach (var v in left.JoinDic)
            {
                if (sqlJoin.Length > 0)
                {
                    sqlJoin.Append(" AND ");
                }
                sqlJoin.Append("(");
                sqlJoin.Append(v.Value);
                sqlJoin.Append("=");
                sqlJoin.Append(right.JoinDic[v.Key]);
                sqlJoin.Append(")");
            }
            var joinStr = $"{JoinStr} {type} JOIN {DBTool.GetTableName(rightQuery.DBModel)} {rightTableAlias} ON {sqlJoin}";

            var sel = new JoinResultMapExpression(resultSelector, Map, rightTableAlias, dynamicParameters, DBModel.GetDBModel_SqlProvider());
            StringBuilder sqlWhere = new StringBuilder(Where);

            var where = new WhereExpression(rightQuery.WhereExpression, rightTableAlias, dynamicParameters, DBModel.GetDBModel_SqlProvider());
            if (!string.IsNullOrEmpty(where.SqlCmd))
            {
                if (sqlWhere.Length > 0)
                {
                    sqlWhere.Append(" AND ");
                }
                sqlWhere.Append(where.SqlCmd);
            }

            return new JoinQueryInfo<TResult>(DBModel, joinStr, JoinTableCount + 1, sel.MapList, sqlWhere.ToString(), dynamicParameters);
        }
        public ISelectResult<TResult> Select<TResult>(Expression<Func<T, TResult>> select, bool firstTableSelectAll = false)
        {
            var p = new DynamicParameters();
            p.AddDynamicParams(Param);
            var selectResult = new JoinSelectExpression(select, Map, p, DBModel.GetDBModel_SqlProvider());
            var selSql = string.Join(",", selectResult.QueryColumns);
            if (firstTableSelectAll)
            {
                if (!string.IsNullOrEmpty(selSql))
                {
                    selSql = "t1.*," + selSql;
                }
                else
                {
                    selSql = "t1.*";
                }
            }
            var isDBModel = typeof(BaseDBModel).IsAssignableFrom(typeof(TResult));
            if (isDBModel)
            {
                selSql = "0 as DBModel_IsBeginChange," + selSql;
            }
            var whereExpression = new JoinWhereExpression(WhereExpression, Map, p, DBModel.GetDBModel_SqlProvider());
            var sql = new StringBuilder($"SELECT {selSql} FROM {JoinStr} WHERE 1=1 {Where} {whereExpression.SqlCmd} ");
            var countSql = new StringBuilder($"SELECT count(1) FROM {JoinStr} WHERE 1=1 {Where} {whereExpression.SqlCmd} ");
            if (OrderbyList.Count > 0)
            {
                sql.Append($" ORDER BY ");
                var olst = OrderbyList.Select(m =>
                  {
                      var order = new JoinOrderExpression(m.Field, Map, p, DBModel.GetDBModel_SqlProvider());
                      return order.SqlCmd + " " + m.OrderBy;
                  });
                sql.Append(string.Join(",", olst) + " ");
            }
            sql.Append(DBModel.GetDBModel_SqlProvider().GetLimit(StarSize, Rows));
            return new SelectResult<TResult>(sql.ToString(), countSql.ToString(), p);
        }

    }
}
