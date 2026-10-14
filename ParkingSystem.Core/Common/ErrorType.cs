namespace ParkingSystem.Core.Common;

// סוגי הכשלים העסקיים. ה-Controller מתרגם כל אחד לקוד HTTP
public enum ErrorType
{
    Validation,    // 400
    Unauthorized,  // 401
    Forbidden,     // 403
    NotFound,      // 404
    Conflict       // 409
}
