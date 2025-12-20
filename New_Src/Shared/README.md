# 🔧 Isatis ICP - Shared

Common utilities and wrappers used across all layers.

## 📋 Overview

این پروژه شامل ابزارهای مشترک بین تمام لایه‌هاست.

## 📁 Structure

```
Shared/
└── Wrapper/
    └── Result.cs    # Generic result wrapper
```

## 📦 Result Wrapper

```csharp
// Base Result
public class Result
{
    public bool Succeeded { get; set; }
    public string[] Messages { get; set; }
    
    public static Result Success();
    public static Result Fail(string message);
}

// Generic Result
public class Result<T> : Result
{
    public T?  Data { get; set; }
    
    public static Result<T> Success(T data);
    public static new Result<T> Fail(string message);
}
```

## 💡 Usage

```csharp
// Success
return Result<UserDto>.Success(userData);

// Failure
return Result<UserDto>.Fail("User not found");

// Check result
if (result. Succeeded)
{
    var data = result.Data;
}
else
{
    var error = result.Messages. FirstOrDefault();
}
```