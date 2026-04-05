namespace Traydio.Commands;

public interface ICommandTextRouter
{
    bool TryDispatch(string commandText);
}

