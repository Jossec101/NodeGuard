using Blazorise;
using FundsManager.Data.Models;
using FundsManager.Data.Repositories;

namespace FundsManager.Helpers;

public static class ValidationHelper
{
    /// <summary>
    /// Validates that the name of the item introduced in the form is not null and is not only whitespaces.
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static void ValidateName(ValidatorEventArgs obj)
    {
        obj.Status = ValidationStatus.Success;
        if (string.IsNullOrWhiteSpace((string)obj.Value))
        {
            obj.ErrorText = "The name cannot be empty";
            obj.Status = ValidationStatus.Error;
        }
    }
    
    public static void ValidateUsername(ValidatorEventArgs obj, List<ApplicationUser> users)
    {
        obj.Status = ValidationStatus.Success;
        if (string.IsNullOrWhiteSpace((string)obj.Value))
        {
            obj.ErrorText = "The Username cannot be empty";
            obj.Status = ValidationStatus.Error;
            return;
        }
        foreach (ApplicationUser user in users)
        {
            if (user.UserName.Equals(obj.Value))
            {
                obj.ErrorText = "A user with the same username already exists";
                obj.Status = ValidationStatus.Error;
                return;
            }
        }
    }

    public static void ValidateAmount(ValidatorEventArgs obj)
    {
        obj.Status = ValidationStatus.Success;
        if (((long)obj.Value) < 20000)
        {
            obj.ErrorText = "The amount must be greater than 20.000";
            obj.Status = ValidationStatus.Error;
        }
    }

    public static void ValidateXPUB(ValidatorEventArgs obj)
    {
        obj.Status = ValidationStatus.Success;
        if (string.IsNullOrWhiteSpace((string)obj.Value))
        {
            obj.ErrorText = "The XPUB field cannot be empty";
            obj.Status = ValidationStatus.Error;
        }

    }

    public static void ValidatePubKey(ValidatorEventArgs obj, List<Node> nodes)
    {
        obj.Status = ValidationStatus.Success;
        if (string.IsNullOrWhiteSpace((string)obj.Value))
        {
            obj.ErrorText = "The PubKey cannot be empty";
            obj.Status = ValidationStatus.Error;
            return;
        }
        obj.Status = ValidationStatus.Success;
        foreach (Node node in nodes)
        {
            if (node.PubKey.Equals(obj.Value))
            {
                obj.ErrorText = "A node with the same pubkey already exists";
                obj.Status = ValidationStatus.Error;
                return;
            }
        }
    }
}