using Esprima;
using Esprima.Ast;
//https://github.com/jquery/esprima

using System.Data.SQLite;
//https://www.nuget.org/packages/Microsoft.Data.Sqlite

using Newtonsoft.Json;
//https://www.newtonsoft.com/json

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PocketnetSqlite
{
    public static class SqliteLab
    {
        public static void QueryTest()
        {
            //Build all the views
            CreateViews();

            //Receive JS query from client
            var queryFromClient = "addresshash === 'PRbQSnk7XHS4twixt6ZBihEtD898JPWcrW'";

            //Get results from database
            var results = QueryTable<PocketnetUser>(queryFromClient);

            //Serialize to JSON and return to the client
            var json = JsonConvert.SerializeObject(results);

            /*

            [
              {
                "Name": "roffle",
                "AddressHash": "PRbQSnk7XHS4twixt6ZBihEtD898JPWcrW",
                "Lang": "en",
                "Avatar": "https://i.imgur.com/PrAksPp.jpg",
                "About": null
              }
            ]
            
            */
        }

        public static T[] QueryTable<T>(string js) where T : new()
        {
            var type = typeof(T);
            var tups = type.GetProperties().Select(prop =>
            {
                var altName = prop.GetCustomAttribute<DbSymbolAttribute>();
                var name = altName?.Name ?? prop.Name;
                return (prop, name);
            });

            //create instance of the visitor
            var visitor = new JsToSqliteVisitor(js);

            //convert the JS to SQL
            var predicate = visitor.GetExpression();

            using (var con = GetConnection())
            {
                //get the table name from the type
                var tableName = type.GetCustomAttribute<DbSymbolAttribute>()?.Name ?? type.Name;

                //get the list of fields from the class
                var fields = string.Join(", ", tups.Select(x => x.name));

                //build the final query;
                var query = $"select {fields} from {tableName} where {predicate}";

                using (var cmd = new SQLiteCommand(query, con))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        var entities = new List<T>();

                        //Iterate through each record rqturned from query
                        while (reader.Read())
                        {
                            var entity = new T();

                            //Iterate through properties to assign values. Reflection in .NET is
                            //a bad choice, but this is just proof oc concept
                            foreach (var tup in tups)
                            {
                                var val = reader[reader.GetOrdinal(tup.name)];

                                if (val is DBNull) continue;

                                tup.prop.SetValue(entity, val);
                            }

                            //Add new entity to collection
                            entities.Add(entity);
                        }

                        //Return collection as array when done
                        return entities.ToArray();
                    }
                }
            }
        }

        public static SQLiteConnection GetConnection()
        {
            var con = new SQLiteConnection(@"Data Source=c:\path\to\database\main.sqlite3");

            con.Open();

            return con;
        }

        public static void CreateViews()
        {
            var sql = @"CREATE VIEW if not exists vWebUsersAll as
select t.Hash,
	t.Id,
	t.Time,
	t.BlockHash,
	t.Height,
	t.String1 as AddressHash,
	t.String2 as ReferrerAddressHash,
	t.Int1    as Registration,
	p.String1 as Lang,
	p.String2 as Name,
	p.String3 as Avatar,
	p.String4 as About,
	p.String5 as Url,
	p.String6 as Pubkey,
	p.String7 as Donations
from Transactions t indexed by Transactions_Type_Last_String1_Height_Id
cross join Payload p on t.Hash = p.TxHash
where
	t.Type = 100 AND
	t.last = 1 and
	t.Height is not null and
	1=1";

            using (var con = GetConnection())
            {
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = sql;

                    var result = cmd.ExecuteNonQuery();
                }
            }
        }
    }

    /* Create a visitor for the JS parser. Write the SQL-equivalent 
     * symbols to the StringBuilder we iterate over the nodes */
    public class JsToSqliteVisitor
    {
        public JsToSqliteVisitor(string js)
        {
            //I wrote this JsToSqliteVisitor class, but JavaScriptParser is by Esprima
            //There is likely a C++ equivalent library that does the same thing
            var parser = new JavaScriptParser(js);

            script = parser.ParseScript();
        }

        StringBuilder sb = new StringBuilder();

        public string GetExpression()
        {
            foreach (var node in script.ChildNodes)
            {
                Visit(node);
            }

            return sb.ToString();
        }

        public void Visit(Node node)
        {
            switch (node.Type)
            {
                case Nodes.BinaryExpression:
                    Visit(node as BinaryExpression);
                    break;
                case Nodes.ExpressionStatement:
                    Visit((node as ExpressionStatement).Expression);
                    break;
                case Nodes.Identifier:
                    Visit(node as Identifier);
                    break;
                case Nodes.Literal:
                    Visit(node as Literal);
                    break;
                default:
                    throw new Exception($"Unexpected node: {node.Type}");
            }
        }

        public void Visit(BinaryExpression node)
        {
            Visit(node.Left);

            Visit(node.Operator);

            Visit(node.Right);
        }

        public Func<Identifier, string> OnIdentifier;
        private Script script;

        public void Visit(Identifier node)
        {
            var name = OnIdentifier?.Invoke(node) ?? node.Name;

            sb.Append(name);
        }

        public void Visit(BinaryOperator op)
        {
            switch (op)
            {
                case BinaryOperator.StrictlyEqual:
                case BinaryOperator.Equal:
                    sb.Append(" = ");
                    break;
                default:
                    throw new Exception($"Unexpected operator: {op}");
            }
        }

        public void Visit(Literal node)
        {
            switch (node.TokenType)
            {
                case TokenType.StringLiteral:
                    sb.Append($"'{node.Value}'");
                    break;
                default:
                    sb.Append(node.Value);
                    break;
            }

        }
    }

    //Annotate class with its equivalent database table/view name
    [DbSymbol("vWebUsersAll")]
    public class PocketnetUser
    {
        public string Name { get; set; }

        //Annotate properties with their database field name if different
        //from class definition
        [DbSymbol("AddressHash")]
        public string Address { get; set; }

        [DbSymbol("Lang")]
        public string Language { get; set; }

        [DbSymbol("Avatar")]
        public string AvatarUrl { get; set; }

        public string About { get; set; }
    }

    public class DbSymbolAttribute : Attribute
    {
        public DbSymbolAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}