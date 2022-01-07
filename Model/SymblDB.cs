using System.Data.SQLite;
using System.Collections.Generic;

namespace Symbl.Concurrency.Model
{
    public interface ISymblDB
    {
        int GetSymblRequestStatusCount(SybmlRequestStatus requestStatus);
        List<SymblRequest> GetTop50SymblRequests();
        void BuildDBModel(bool dropIfExist = false);
        int Insert(SymblRequest symblRequest);
        int Update(SymblRequest symblRequest);
    }

    public class SymblDB : ISymblDB
    {
        readonly string connectionString;
        public SymblDB(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public int Insert(SymblRequest symblRequest)
        {
            int response = 0;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = $"INSERT INTO SymblRequests (requestId,conversationId,status) values('{symblRequest.RequestId}', '{symblRequest.ConversationId}','{symblRequest.Status}')";
                    response = command.ExecuteNonQuery();
                }
            }

            return response;
        }

        public int Update(SymblRequest symblRequest)
        {
            int response = 0;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = $"UPDATE SymblRequests SET status = '{symblRequest.Status}' where Id={symblRequest.Id}";
                    response = command.ExecuteNonQuery();
                }
            }

            return response;
        }

        public int GetSymblRequestStatusCount(SybmlRequestStatus requestStatus)
        {
            int count = 0;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = $"SELECT count(1) FROM SymblRequests where status = '{requestStatus}';";
                    var reader = command.ExecuteReader();
                    if (reader.Read())
                        return int.Parse(reader[0].ToString());
                }
            }

            return count;
        }


        public List<SymblRequest> GetTop50SymblRequests()
        {
            List<SymblRequest> symblRequests = new List<SymblRequest>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = $"SELECT * FROM SymblRequests " +
                        $"where status != '{SybmlRequestStatus.Completed}' limit 50";
                     
                    var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        symblRequests.Add(new SymblRequest
                        {
                            Id = int.Parse(reader["id"].ToString()),
                            RequestId = reader["requestId"].ToString(),
                            ConversationId = reader["conversationId"].ToString(),
                            Status = reader["status"].ToString()
                        }); 
                    }
                }
            }

            return symblRequests;
        }

        public void BuildDBModel(bool dropIfExist = false)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(connection))
                {
                    if (dropIfExist)
                    {
                        command.CommandText = "DROP TABLE IF EXISTS SymblRequests";
                        command.ExecuteNonQuery();
                    }

                    command.CommandText = @"CREATE TABLE IF NOT EXISTS SymblRequests(
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        requestId VARCHAR(100),
                        conversationId VARCHAR(100),
                        status VARCHAR(20))";

                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
