﻿
using Breeze.Sharp.Core;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Breeze.Sharp {

  /// <summary>
  /// 
  /// </summary>
  public class EntityTypeBuilder<TEntity> where TEntity:IEntity  {

    public EntityTypeBuilder() {
      EntityType = MetadataStore.Instance.GetEntityType(typeof(TEntity), true);
      if (EntityType == null) {
        EntityType = new EntityType();
        EntityType.ClrType = typeof(TEntity);
        // can't add until later - no key props yet
        // MetadataStore.Instance.AddEntityType(EntityType);
      }
    }

    public EntityType EntityType {
      get;
      private set;
    }

    /// <summary>
    /// Returns null if not found
    /// </summary>
    /// <typeparam name="TValue"></typeparam>
    /// <param name="propExpr"></param>
    /// <returns></returns>

    public DataPropertyBuilder DataProperty<TValue>(Expression<Func<TEntity, TValue>> propExpr) {
      var pInfo = GetPropertyInfo(propExpr);
      var dp = EntityType.GetDataProperty(pInfo.Name);
      if (dp != null) {
        return new DataPropertyBuilder(dp);
      }
      var propType = pInfo.PropertyType;
      dp = new DataProperty();
      dp.Name = pInfo.Name;
      // TODO: handle isScalar
      if (typeof (IComplexObject).IsAssignableFrom(propType)) {
        dp.ComplexType = MetadataStore.Instance.GetComplexType(propType);
        dp.IsNullable = false;
        // complex Objects do not have defaultValues currently
      } else {
        dp.DataType = DataType.FromClrType(propType);
        dp.IsNullable = TypeFns.IsNullableType(propType);
        dp.DefaultValue = dp.IsNullable ? null : dp.DataType.DefaultValue;
      }
      
      EntityType.AddDataProperty(dp);

      return new DataPropertyBuilder(dp);
    }


    public NavigationPropertyBuilder<TEntity, TTarget> NavigationProperty<TTarget>(
      Expression<Func<TEntity, TTarget>> propExpr) where TTarget:IEntity {
      var pInfo = GetPropertyInfo(propExpr);
      return GetNavPropBuilder<TTarget>(pInfo, true);
    }

    public NavigationPropertyBuilder<TEntity, TTarget> NavigationProperty<TTarget>(
      Expression<Func<TEntity, NavigationSet<TTarget>>> propExpr) where TTarget: IEntity {
      var pInfo = GetPropertyInfo(propExpr);
      return GetNavPropBuilder<TTarget>(pInfo, false);
    }

    private NavigationPropertyBuilder<TEntity, TTarget> GetNavPropBuilder<TTarget>(PropertyInfo pInfo, bool isScalar) where TTarget : IEntity {
      var np = EntityType.GetNavigationProperty(pInfo.Name);
      if (np == null) {
        np = new NavigationProperty();
        np.Name = pInfo.Name;
        np.IsScalar = isScalar;
        np.EntityTypeName = TypeNameInfo.FromClrTypeName(typeof (TTarget).FullName).Name;
        // may change later
        np.AssociationName = EntityType.Name + "_" + np.Name;
        EntityType.AddNavigationProperty(np);
      }
      return new NavigationPropertyBuilder<TEntity, TTarget>(this, np);
    }

    internal PropertyInfo GetPropertyInfo<TValue>(Expression<Func<TEntity, TValue>> propExpr) {
      var lambda = propExpr as LambdaExpression;
      if (lambda == null) throw new ArgumentNullException("propExpr");
      var memberExpr = lambda.Body as MemberExpression;
      if (memberExpr == null) {
        throw new Exception("Unable to resolve property for: " + propExpr);
      }
      var pInfo = memberExpr.Member as PropertyInfo;
      if (pInfo == null) {
        throw new Exception("Unable to resolve " + propExpr + " as a property");
      }
      return pInfo;
    }
  }

  public class DataPropertyBuilder {
    public DataPropertyBuilder(DataProperty dp) {
      DataProperty = dp;
    }

    public DataPropertyBuilder IsNullable() {
      DataProperty.IsNullable = true;
      return this;
    }
    public DataPropertyBuilder IsRequired() {
      DataProperty.IsNullable = false;
      return this;
    }

    public DataPropertyBuilder IsPartOfKey() {
      DataProperty.IsPartOfKey = true;
      DataProperty.IsNullable = false;
      var et = DataProperty.ParentType as EntityType;
      if (et != null) {
        var isAlreadyKey = et._keyProperties.Contains(DataProperty);
        if (!isAlreadyKey) {
          et._keyProperties.Add(DataProperty);
        }
      }
      return this;
    }

    public DataPropertyBuilder IsAutoIncrementing() {
      DataProperty.IsAutoIncrementing = true;
      var et = DataProperty.ParentType as EntityType;
      if (et != null) {
        et.AutoGeneratedKeyType = AutoGeneratedKeyType.Identity;
      }
      return this;
    }

    public DataPropertyBuilder DefaultValue(Object defaultValue) {
      // TODO: check if valid
      DataProperty.DefaultValue = defaultValue;
      return this;
    }

    public DataPropertyBuilder ConcurrencyMode(ConcurrencyMode mode) {
      DataProperty.ConcurrencyMode = mode;
      return this;
    }

    public DataPropertyBuilder MaxLength(int? maxLength) {
      DataProperty.MaxLength = maxLength;
      return this;
    }

    public DataPropertyBuilder IsScalar(bool isScalar) {
      DataProperty.IsScalar = isScalar;
      return this;
    }

    public DataProperty DataProperty { get; private set; }
    }


  public class NavigationPropertyBuilder<TEntity, TTarget> where TEntity: IEntity where TTarget: IEntity {

    public NavigationPropertyBuilder(EntityTypeBuilder<TEntity> etb, NavigationProperty np) {
      _etb = etb;
      NavigationProperty = np;
    }

    public NavigationPropertyBuilder<TEntity, TTarget> HasForeignKey<TValue>(Expression<Func<TEntity, TValue>> propExpr) {
      if (!NavigationProperty.IsScalar) {
        throw new Exception("Can only call 'WithForeignKey' on a scalar NavigationProperty");
      }
      var dpb = _etb.DataProperty(propExpr);
      var fkProp = dpb.DataProperty;
      fkProp.IsForeignKey = true;

      fkProp.RelatedNavigationProperty = NavigationProperty;
      var fkNames = NavigationProperty._foreignKeyNames;
      if (!fkNames.Contains(fkProp.Name)) {
        fkNames.Add(fkProp.Name);
      }
      return this;
    }

    // only needed in unusual cases.
    public NavigationPropertyBuilder<TEntity, TTarget> HasInverseForeignKey<TValue>(Expression<Func<TTarget, TValue>> propExpr) {
      var invEtb = new EntityTypeBuilder<TTarget>();
      var invDpBuilder = invEtb.DataProperty(propExpr);
      var invFkProp = invDpBuilder.DataProperty;
      invFkProp.IsForeignKey = true;

      invFkProp.InverseNavigationProperty = NavigationProperty;
      var invFkNames = NavigationProperty._invForeignKeyNames;
      if (!invFkNames.Contains(invFkProp.Name)) {
        invFkNames.Add(invFkProp.Name);
      }
      return this;
    }

    public NavigationPropertyBuilder<TEntity, TTarget> HasInverse(Expression<Func<TTarget, TEntity>> propExpr) {
      var invEtb = new EntityTypeBuilder<TTarget>();
      var invNp = invEtb.NavigationProperty(propExpr).NavigationProperty;
      return HasInverseCore(invNp);
      
    }

    public NavigationPropertyBuilder<TEntity, TTarget> HasInverse(Expression<Func<TTarget, NavigationSet<TEntity>>> propExpr) {
      var invEtb = new EntityTypeBuilder<TTarget>();
      var invNp = invEtb.NavigationProperty(propExpr).NavigationProperty;
      return HasInverseCore(invNp);
    }

    private NavigationPropertyBuilder<TEntity, TTarget> HasInverseCore(NavigationProperty invNp) {
      NavigationProperty.Inverse = invNp;
      invNp.Inverse = NavigationProperty;
      invNp.AssociationName = NavigationProperty.AssociationName;
      return this;
    }

    private readonly EntityTypeBuilder<TEntity> _etb;
    public NavigationProperty NavigationProperty { get; private set; }
  }
}