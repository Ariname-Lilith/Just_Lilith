namespace Just_Lilith.Core.Llm;

public interface ISecretProtector
{
	string Protect(string plaintext);

	string Unprotect(string protectedText);
}
