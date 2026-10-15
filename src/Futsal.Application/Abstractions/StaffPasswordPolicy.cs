namespace Futsal.Application.Abstractions;
public static class StaffPasswordPolicy
{
 private static readonly HashSet<string> Common=new(StringComparer.OrdinalIgnoreCase){"password","password123","password1234","1234567890","12345678901","qwerty12345","admin12345","futsal12345","nepal12345","abcdefghij","letmein12345","welcome123"};
 public static bool Accepts(string? password)=>password!=null&&password.Length>=10&&password.Length<=128&&password.Distinct().Count()>=5&&!Common.Contains(password);
}
