// Program.cs 或测试类中调用
public static void Main()
{
    using var context = new AppDbContext();
    
    // 确保数据库已创建
    context.Database.EnsureCreated();

    var generator = new TestDataGenerator(context);
    generator.GenerateAllData();
}